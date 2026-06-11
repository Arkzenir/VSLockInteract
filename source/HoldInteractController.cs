using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace LockInteract
{
    /// <summary>
    /// Manages the hold-to-interact logic on the client side.
    ///
    /// When the player right-clicks a locked block that passes the configured filters:
    ///   1. The interact action is suppressed (PreventSubsequent).
    ///   2. A hold timer starts and the progress overlay is shown.
    ///   3. If the player holds for the configured duration, a packet is sent to the
    ///      server to fire OnBlockInteractStart server-side (opening the door/chest).
    ///   4. Releasing early or looking away cancels the hold.
    ///
    /// Lock detection uses ModSystemBlockReinforcement.GetReinforcment(), which is
    /// synced to clients per-chunk via the blockreinforcement network channel.
    ///
    /// Multiblock structures (large gates, multi-part doors) are resolved via
    /// IMultiblockOffset.GetControlBlockPos(), the canonical VS API for finding
    /// the origin block of any multiblock structure.
    ///
    /// CarryOn compatibility: when CarryOn is installed and the player is carrying
    /// a block in their hands, LockInteract yields entirely. CarryOn's interact-
    /// while-carrying is handled server-side; there is no client-side hold timer
    /// to sequence against.
    /// </summary>
    public class HoldInteractController : IDisposable
    {
        private readonly ICoreClientAPI          _api;
        private readonly LockInteractConfig      _config;
        private readonly ProgressOverlayRenderer _overlay;
        private readonly IClientNetworkChannel   _channel;

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

        /// <summary>True if the CarryOn mod is present in this game session.</summary>
        private readonly bool _carryOnPresent;

        /// <summary>
        /// True if OverhaulLib is present, meaning the "manipulationSpeed" entity
        /// stat is registered and meaningful. Checked once at construction.
        /// </summary>
        private readonly bool _overhaulLibPresent;

        // ── Interaction state ─────────────────────────────────────────────────

        private bool      _holding;   // hold timer is running
        private float     _timeHeld;  // seconds held so far
        private BlockPos? _targetPos; // position the player is aiming at
        private BlockPos? _lockPos;   // position carrying the lock (may differ for multiblock)

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
                api.Logger.Notification("[LockInteract] CarryOn detected — LockInteract yields when player is carrying.");

            _overhaulLibPresent = api.ModLoader.IsModEnabled("overhaullib");
            if (_overhaulLibPresent && _config.UseManipulationSpeed)
                api.Logger.Notification("[LockInteract] OverhaulLib detected — manipulation speed affects hold duration.");

            _onEntityAction = OnEntityAction;
            _api.Input.InWorldAction += _onEntityAction;
        }

        // ── Input event ───────────────────────────────────────────────────────

        private void OnEntityAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (!_config.Enabled) return;

            // Release — cancel any active hold
            if (!on && action == EnumEntityAction.InWorldRightMouseDown)
            {
                if (_holding) Cancel();
                return;
            }

            if (!on || action != EnumEntityAction.InWorldRightMouseDown) return;

            // Already holding — suppress the event so the block doesn't open immediately
            if (_holding)
            {
                handled = EnumHandling.PreventSubsequent;
                return;
            }

            // Yield to CarryOn when the player is carrying a block in their hands
            if (_carryOnPresent && IsCarryingInHands()) return;

            var blockSel = _api.World.Player?.CurrentBlockSelection;
            if (blockSel == null) return;

            BlockPos? lockPos = FindLockPos(blockSel.Position);
            if (lockPos == null) return;

            handled = EnumHandling.PreventSubsequent;
            BeginHold(blockSel.Position.Copy(), lockPos);
        }

        // ── CarryOn helper ────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when CarryOn has a block carried in the player's hands slot.
        ///
        /// When CarryOn carries a block in hands it replaces entity.RightHandItemSlot
        /// with a LockedItemSlot instance, preventing item use while carrying.
        /// Checking the runtime type name requires no compile-time dependency on CarryOn.
        /// </summary>
        private bool IsCarryingInHands()
        {
            var slot = _api.World.Player?.Entity?.RightHandItemSlot;
            return slot?.GetType().Name == "LockedItemSlot";
        }

        // ── Hold logic ────────────────────────────────────────────────────────

        private void BeginHold(BlockPos targetPos, BlockPos lockPos)
        {
            _holding   = true;
            _timeHeld  = 0f;
            _targetPos = targetPos;
            _lockPos   = lockPos;
        }

        /// <summary>Called every client game tick by the mod system.</summary>
        public void Tick(float dt)
        {
            if (!_holding) return;
            if (!_config.Enabled) { Cancel(); return; }

            // Cancel if the player releases the interact key
            if (!(_api.World.Player?.Entity?.Controls?.RightMouseDown ?? false)) { Cancel(); return; }

            // Cancel if the player looks away from the target block
            var blockSel = _api.World.Player?.CurrentBlockSelection;
            if (blockSel == null || !blockSel.Position.Equals(_targetPos)) { Cancel(); return; }

            // Guard against a bad dt (NaN/Infinity/negative) poisoning the timer
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) return;

            _timeHeld += dt;
            float progress = Math.Min(1f, _timeHeld / Math.Max(0.01f, EffectiveHoldTime()));
            _overlay.SetProgress(progress);

            if (progress >= 1.0f)
                Complete(blockSel);
        }

        /// <summary>
        /// Computes the hold duration in seconds, optionally scaled by the player's
        /// "manipulationSpeed" stat (from OverhaulLib / CombatOverhaul).
        ///
        /// CombatOverhaul scales its own action times as time / manipulationSpeed,
        /// where 1.0 is neutral, &gt;1.0 is faster, &lt;1.0 is slower. We mirror that,
        /// blended by ManipulationSpeedWeight:
        ///   effectiveSpeed = lerp(1.0, manipulationSpeed, weight)
        ///   effectiveTime  = HoldTime / effectiveSpeed
        ///
        /// At weight 0 the stat has no effect; at weight 1 it matches CombatOverhaul exactly.
        /// Falls back to the raw HoldTime when OverhaulLib is absent or the feature is off.
        /// </summary>
        private float EffectiveHoldTime()
        {
            // Sanitise the configured base time: reject NaN, infinity, and
            // non-positive values that a user could put in the config file.
            float baseTime = _config.HoldTime;
            if (float.IsNaN(baseTime) || float.IsInfinity(baseTime) || baseTime <= 0f)
                baseTime = 0.8f;

            if (!_config.UseManipulationSpeed || !_overhaulLibPresent)
                return baseTime;

            var entity = _api.World.Player?.Entity;
            if (entity == null) return baseTime;

            // Standard VS stat API — no compile-time dependency on OverhaulLib.
            // Neutral value is 1.0; GetBlended returns that when no modifiers apply.
            float manipulationSpeed = entity.Stats.GetBlended("manipulationSpeed");
            // NaN/Infinity-safe: any comparison with NaN is false, so check explicitly.
            if (float.IsNaN(manipulationSpeed) || float.IsInfinity(manipulationSpeed) || manipulationSpeed <= 0f)
                return baseTime;

            float weight = _config.ManipulationSpeedWeight;
            if (float.IsNaN(weight) || float.IsInfinity(weight)) weight = 1f;
            weight = GameMath.Clamp(weight, 0f, 1f);

            float effectiveSpeed = 1f + (manipulationSpeed - 1f) * weight;
            if (float.IsNaN(effectiveSpeed) || float.IsInfinity(effectiveSpeed) || effectiveSpeed <= 0f)
                return baseTime;

            float result = baseTime / effectiveSpeed;
            if (float.IsNaN(result) || float.IsInfinity(result) || result <= 0f)
                return baseTime;

            return result;
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

            // Send the lock position to the server. The server runs OnBlockInteractStart
            // which performs the actual access check and opens the block. We use the
            // lock position (origin of the multiblock) rather than the clicked face.
            var target = _lockPos ?? blockSel.Position;
            _channel.SendPacket(new LockInteractUseMessage { X = target.X, Y = target.Y, Z = target.Z });
        }

        private void Cancel()
        {
            _holding   = false;
            _timeHeld  = 0f;
            _targetPos = null;
            _lockPos   = null;
            _overlay.Hide();
        }

        // ── Lock detection ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the lock position to use for this interaction, or null if no
        /// delay should be applied.
        ///
        /// Multiblock resolution: BlockMultiblock sub-blocks implement IMultiblockOffset.
        /// GetControlBlockPos() returns the origin block — the one that carries the
        /// reinforcement data. For plain single blocks the position is used as-is.
        ///
        /// Lock data comes from ModSystemBlockReinforcement, which is synced to clients
        /// per-chunk, so bre.Locked is accurate client-side.
        /// </summary>
        private BlockPos? FindLockPos(BlockPos clicked)
        {
            var block = _api.World.BlockAccessor.GetBlock(clicked);

            // Resolve the origin block for multiblock structures (gates, large doors)
            BlockPos lockPos = (block is IMultiblockOffset mb)
                ? mb.GetControlBlockPos(clicked.Copy())
                : clicked;

            var bre = Bre?.GetReinforcment(lockPos);
            if (bre == null || !bre.Locked) return null;

            // ApplyToPersonalLocks=false: sole individual owner gets no delay
            if (!_config.ApplyToPersonalLocks)
            {
                var playerUid    = _api.World.Player?.PlayerUID;
                bool isSoleOwner = bre.PlayerUID == playerUid && bre.GroupUid == 0;
                if (isSoleOwner) return null;
            }

            var originCode = _api.World.BlockAccessor.GetBlock(lockPos)?.Code?.ToString() ?? "";

            // Deny-list takes precedence over allow-list
            if (_config.BlockCodeDenyList.Count > 0 && MatchesGlobList(originCode, _config.BlockCodeDenyList))
                return null;

            // Empty allow-list means all locked blocks; non-empty restricts to matches only
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
