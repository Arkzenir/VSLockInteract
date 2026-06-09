using System.Collections.Generic;

namespace LockInteract
{
    public class LockInteractConfig
    {
        // ── Feature toggle ────────────────────────────────────────────────────

        /// <summary>
        /// Master switch. Set to false to completely disable the mod.
        /// Default: true
        /// </summary>
        public bool Enabled { get; set; } = true;

        // ── Timing ────────────────────────────────────────────────────────────

        /// <summary>
        /// How long (in seconds) the player must hold the interact key to open
        /// a locked block. Matches the feel of CarryOn's pick-up delay.
        /// Default: 0.8
        /// </summary>
        public float HoldTime { get; set; } = 0.8f;

        // ── Scope ─────────────────────────────────────────────────────────────

        /// <summary>
        /// When true, ALL claimed blocks cause a delay — including blocks the
        /// player personally owns as sole owner (no group involved).
        ///
        /// When false, only blocks owned by a group (or another player entirely)
        /// cause a delay. Blocks where the player is the sole individual owner
        /// are opened instantly.
        ///
        /// Default: true
        /// </summary>
        public bool ApplyToPersonalLocks { get; set; } = false;

        // ── Block filtering ───────────────────────────────────────────────────

        /// <summary>
        /// Optional allow-list of block codes (or glob patterns with *) that
        /// the mod will restrict. If non-empty only blocks whose code matches
        /// an entry in this list are affected (regardless of claim status).
        ///
        /// Glob examples:
        ///   "game:chest-*"   – all chest variants
        ///   "game:door-*"    – all doors
        ///
        /// Leave empty to use claim-based detection instead.
        /// Default: [] (empty – claim-based mode)
        /// </summary>
        public List<string> BlockCodeAllowList { get; set; } = new() { "game:door-*" };

        /// <summary>
        /// Optional deny-list of block codes (or glob patterns with *) that
        /// the mod will NEVER restrict, even when claim-based detection would
        /// normally trigger. Takes precedence over BlockCodeAllowList.
        /// Default: [] (empty – nothing excluded)
        /// </summary>
        public List<string> BlockCodeDenyList { get; set; } = new();

        // ── Display ───────────────────────────────────────────────────────────

        /// <summary>
        /// Show the circular hold-progress overlay (same visual as CarryOn).
        /// Default: true
        /// </summary>
        public bool ShowProgressOverlay { get; set; } = true;

        /// <summary>
        /// Play a soft "click" sound when the interaction completes.
        /// Default: true
        /// </summary>
        public bool PlayCompletionSound { get; set; } = true;

        /// <summary>
        /// Show a HUD tooltip while the player is holding down the interact button.
        /// Default: true
        /// </summary>
        public bool ShowHoldHint { get; set; } = true;
    }
}
