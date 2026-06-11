using System.Collections.Generic;

namespace LockInteract
{
    public class LockInteractConfig
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Base seconds the player must hold interact before the action fires.</summary>
        public float HoldTime { get; set; } = 0.8f;

        /// <summary>
        /// When false (default), blocks the player owns personally (sole individual
        /// lock, no group) open instantly. When true, the delay applies to all locks
        /// including your own.
        /// </summary>
        public bool ApplyToPersonalLocks { get; set; } = false;

        // ── Manipulation Speed integration (CombatOverhaul / OverhaulLib) ──────

        /// <summary>
        /// When true and the OverhaulLib mod is loaded, the player's
        /// "manipulationSpeed" stat scales the hold duration. Higher manipulation
        /// speed shortens the hold; lower lengthens it. Has no effect if OverhaulLib
        /// is not installed.
        /// Default: true
        /// </summary>
        public bool UseManipulationSpeed { get; set; } = true;

        /// <summary>
        /// How strongly manipulation speed affects the hold duration, 0.0 to 1.0.
        ///   0.0 — manipulation speed has no effect (hold time is constant).
        ///   1.0 — full effect, identical to how CombatOverhaul scales its own actions
        ///         (effective time = HoldTime / manipulationSpeed).
        /// Values in between blend linearly between "no effect" and "full effect".
        /// Default: 1.0
        /// </summary>
        public float ManipulationSpeedWeight { get; set; } = 1.0f;

        // ── Block filtering ───────────────────────────────────────────────────

        /// <summary>
        /// If non-empty, only locked blocks whose code matches an entry are delayed.
        /// Empty means all locked blocks are affected. Supports * wildcards.
        /// Default: ["game:door-*"]
        /// </summary>
        public List<string> BlockCodeAllowList { get; set; } = new() { "game:door-*" };

        /// <summary>
        /// Locked blocks whose code matches an entry are never delayed.
        /// Takes precedence over BlockCodeAllowList. Supports * wildcards.
        /// </summary>
        public List<string> BlockCodeDenyList { get; set; } = new();

        // ── Display ───────────────────────────────────────────────────────────

        public bool ShowProgressOverlay { get; set; } = true;
        public bool PlayCompletionSound { get; set; } = true;
        public bool ShowHoldHint        { get; set; } = true;
    }
}
