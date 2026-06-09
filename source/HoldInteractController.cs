using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace LockInteract
{
    public class HoldInteractController : IDisposable
    {
        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly ICoreClientAPI         _api;
        private readonly LockInteractConfig     _config;
        private readonly ProgressOverlayRenderer _overlay;
        private readonly IClientNetworkChannel  _channel;

        // ── BlockReinforcement (lazy) ─────────────────────────────────────────

        private ModSystemBlockReinforcement? _bre;
        private bool _breSearched;
        private ModSystemBlockReinforcement? Bre
        {
            get
            {
                if (!_breSearched)
                {
                    _bre = _api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
                    _breSearched = true;
                    if (_bre == null)
                        _api.Logger.Warning("[LockInteract] ModSystemBlockReinforcement not found.");
                }
                return _bre;
            }
        }

        // ── CarryOn compat ────────────────────────────────────────────────────

        private readonly bool _carryOnPresent;
        private const string CarryOnWatchedKey = "entityCarried";
        private const string CarryOnHandsSlot  = "Hands";

        // ── Interaction state ─────────────────────────────────────────────────

        private bool      _holding;
        private float     _timeHeld;
        private BlockPos? _targetPos;   // position of the block being held on
        private BlockPos? _lockPos;     // position where the lock data actually lives

        // ── Event subscription ────────────────────────────────────────────────

        private OnEntityAction? _onEntityAction;

        // ── Constructor ───────────────────────────────────────────────────────

        public HoldInteractController(ICoreClientAPI api, LockInteractConfig config, IClientNetworkChannel channel)
        {
            _api     = api;
            _config  = config;
            _channel = channel;
            _overlay = new ProgressOverlayRenderer(api, config);

            _carryOnPresent = api.ModLoader.IsModEnabled("carryon");
            if (_carryOnPresent)
                api.Logger.Notification("[LockInteract] CarryOn detected — will yield when a block is being carried.");

            _onEntityAction = OnEntityAction;
            _api.Input.InWorldAction += _onEntityAction;
        }

        // ── Input event ───────────────────────────────────────────────────────

        private void OnEntityAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (!_config.Enabled) return;

            if (!on && action == EnumEntityAction.InWorldRightMouseDown)
            {
                if (_holding) Cancel();
                return;
            }

            if (!on || action != EnumEntityAction.InWorldRightMouseDown) return;

            if (_carryOnPresent && IsCarryingInHands()) return;

            // If already holding, just keep suppressing the action — don't restart.
            // InWorldRightMouseDown fires every frame the button is held, not only on press.
            if (_holding)
            {
                handled = EnumHandling.PreventDefault;
                return;
            }

            var blockSel = _api.World.Player?.CurrentBlockSelection;
            if (blockSel == null) return;

            BlockPos? lockPos = FindLockPos(blockSel.Position);
            if (lockPos == null) return;

            handled = EnumHandling.PreventDefault;
            BeginHold(blockSel.Position.Copy(), lockPos);
        }

        // ── CarryOn detection ─────────────────────────────────────────────────

        private bool IsCarryingInHands()
        {
            var entity = _api.World.Player?.Entity;
            if (entity == null) return false;
            var carriedRoot = entity.WatchedAttributes.GetTreeAttribute(CarryOnWatchedKey);
            if (carriedRoot == null) return false;
            var handsTree = carriedRoot.GetTreeAttribute(CarryOnHandsSlot);
            return handsTree != null && handsTree.Count > 0;
        }

        // ── Hold logic ────────────────────────────────────────────────────────

        private void BeginHold(BlockPos targetPos, BlockPos lockPos)
        {
            _holding    = true;
            _timeHeld   = 0f;
            _targetPos  = targetPos;
            _lockPos    = lockPos;
        }

        public void Tick(float dt)
        {
            if (!_holding) return;
            if (!_config.Enabled) { Cancel(); return; }

            if (!(_api.World.Player?.Entity?.Controls?.RightMouseDown ?? false)) { Cancel(); return; }

            var blockSel = _api.World.Player?.CurrentBlockSelection;
            if (blockSel == null || !blockSel.Position.Equals(_targetPos)) { Cancel(); return; }

            if (_carryOnPresent && IsCarryingInHands()) { Cancel(); return; }

            _timeHeld += dt;
            float progress = Math.Min(1f, _timeHeld / Math.Max(0.01f, _config.HoldTime));
            _overlay.SetProgress(progress);

            if (progress >= 1.0f)
                Complete(blockSel);
        }

        private void Complete(BlockSelection blockSel)
        {
            _holding = false;
            _overlay.Hide();

            if (_config.PlayCompletionSound)
            {
                _api.World.PlaySoundAt(
                    new AssetLocation("sounds/block/door"),
                    blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z,
                    _api.World.Player, false, 8f, 0.5f);
            }

            // Tell the server to run OnBlockInteractStart for this block.
            // We send the position that has the lock (may differ from the clicked
            // face on multiblock doors), but we use the clicked position as the
            // interaction target so the server uses the right BlockSelection.
            var target = _lockPos ?? blockSel.Position;
            _channel.SendPacket(new LockInteractUseMessage
            {
                X = target.X,
                Y = target.Y,
                Z = target.Z
            });
        }

        private void Cancel()
        {
            _holding    = false;
            _timeHeld   = 0f;
            _targetPos  = null;
            _lockPos    = null;
            _overlay.Hide();
        }

        // ── Lock detection ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the lock position to use for this click, or null if no delay is needed.
        ///
        /// Resolution: if the clicked block is a multiblock sub-block (BlockMultiblock),
        /// IMultiblockOffset.GetControlBlockPos() returns the origin block — the one that
        /// actually carries the reinforcement data. For any plain block it stays as-is.
        /// This single call handles all door/gate sizes at any orientation with no
        /// special-casing.
        /// </summary>
        private BlockPos? FindLockPos(BlockPos clicked)
        {
            var block = _api.World.BlockAccessor.GetBlock(clicked);

            // 1. Resolve to the control/origin block for multiblock structures.
            //    BlockMultiblock sub-blocks implement IMultiblockOffset; plain blocks don't.
            BlockPos lockPos = (block is IMultiblockOffset mb)
                ? mb.GetControlBlockPos(clicked.Copy())
                : clicked;

            // 2. Must be locked — always required regardless of any other config.
            var bre = Bre?.GetReinforcment(lockPos);
            if (bre == null || !bre.Locked) return null;

            // 3. ApplyToPersonalLocks=false: no delay when player is sole individual owner.
            if (!_config.ApplyToPersonalLocks)
            {
                var playerUid    = _api.World.Player?.PlayerUID;
                bool isSoleOwner = bre.PlayerUID == playerUid && bre.GroupUid == 0;
                if (isSoleOwner) return null;
            }

            // 4. Deny-list: explicitly excluded block codes are never delayed.
            var originCode = _api.World.BlockAccessor.GetBlock(lockPos)?.Code?.ToString() ?? "";
            if (_config.BlockCodeDenyList.Count > 0 && MatchesGlobList(originCode, _config.BlockCodeDenyList))
                return null;

            // 5. Allow-list: if set, only delay blocks whose code matches.
            //    An empty allow-list means "all locked blocks" (default behaviour).
            if (_config.BlockCodeAllowList.Count > 0 && !MatchesGlobList(originCode, _config.BlockCodeAllowList))
                return null;

            return lockPos;
        }

        // ── Glob matching ─────────────────────────────────────────────────────

        private static bool MatchesGlobList(string code, List<string> patterns)
        {
            foreach (var pattern in patterns)
                if (GlobMatch(pattern, code)) return true;
            return false;
        }

        private static bool GlobMatch(string pattern, string value)
        {
            if (pattern == "*") return true;
            int star = pattern.IndexOf('*');
            if (star < 0)
                return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
            string prefix = pattern[..star];
            string suffix = pattern[(star + 1)..];
            if (value.Length < prefix.Length + suffix.Length) return false;
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (!value.EndsWith(suffix,   StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_onEntityAction != null)
            {
                _api.Input.InWorldAction -= _onEntityAction;
                _onEntityAction = null;
            }
            _overlay.Dispose();
        }
    }
}
