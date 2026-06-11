using System;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace LockInteract
{
    /// <summary>
    /// Client-to-server message sent when a hold interaction completes.
    /// The server runs OnBlockInteractStart at the given position, which performs
    /// the actual access check and opens the block. We send the lock origin position
    /// (resolved from any multiblock) rather than the clicked face position.
    /// </summary>
    [ProtoContract]
    public class LockInteractUseMessage
    {
        [ProtoMember(1)] public int X;
        [ProtoMember(2)] public int Y;
        [ProtoMember(3)] public int Z;
    }

    public class LockInteractModSystem : ModSystem
    {
        private const string ChannelName = "lockinteract";

        private LockInteractConfig        _config     = new();
        private HoldInteractController?   _controller;
        private IClientNetworkChannel?    _clientChannel;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        // ── Client ────────────────────────────────────────────────────────────

        public override void StartClientSide(ICoreClientAPI api)
        {
            _config = LoadConfig(api);

            _clientChannel = api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<LockInteractUseMessage>();

            _controller = new HoldInteractController(api, _config, _clientChannel);
            api.Event.RegisterGameTickListener(dt => _controller?.Tick(dt), 0);
        }

        // ── Server ────────────────────────────────────────────────────────────

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<LockInteractUseMessage>()
                .SetMessageHandler<LockInteractUseMessage>(OnUseMessage);
        }

        /// <summary>
        /// Fires OnBlockInteractStart server-side when a client completes a hold.
        /// The server performs the actual access check (reinforcement lock, land claim)
        /// and sends the appropriate response packet (e.g. open inventory) back to the client.
        /// </summary>
        private static void OnUseMessage(IServerPlayer player, LockInteractUseMessage msg)
        {
            var pos   = new BlockPos(msg.X, msg.Y, msg.Z, player.Entity.Pos.Dimension);
            var world = player.Entity.World;
            var block = world.BlockAccessor.GetBlock(pos);
            if (block == null || block.Id == 0) return;

            var blockSel = new BlockSelection { Position = pos, Block = block };
            block.OnBlockInteractStart(world, player, blockSel);
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public override void Dispose()
        {
            _controller?.Dispose();
        }

        // ── Config ────────────────────────────────────────────────────────────

        private static LockInteractConfig LoadConfig(ICoreClientAPI api)
        {
            LockInteractConfig? cfg = null;
            try   { cfg = api.LoadModConfig<LockInteractConfig>("lockinteract.json"); }
            catch (Exception ex)
            {
                api.Logger.Error($"[LockInteract] Failed to parse ModConfig/lockinteract.json: {ex.Message}. Using defaults.");
            }

            if (cfg != null) return cfg;

            // No user config yet — write defaults so the file exists for editing
            cfg = new LockInteractConfig();
            try   { api.StoreModConfig(cfg, "lockinteract.json"); }
            catch (Exception ex)
            {
                api.Logger.Warning($"[LockInteract] Could not write default config: {ex.Message}");
            }
            return cfg;
        }
    }
}
