# LockInteract

Adds a short hold-to-interact delay when opening locked blocks. Instead of a locked door swinging open the instant you right-click, you must hold the interact button for a brief moment — creating intentional friction that makes locks feel more meaningful.

---

## How it works

When you right-click a locked block that passes the configured filters:

1. The interact action is suppressed.
2. A circular progress ring appears at the crosshair with a "Hold to open…" label below it.
3. Hold the interact button for the configured duration (default 0.8s) to open the block.
4. Release early or look away to cancel.

---

## Default behaviour

Out of the box, the mod restricts **locked doors** (`game:door-*`) only. Chests, crates, and other containers are unaffected unless you add them to the allow-list.

Players who own a door personally (sole individual lock, no group) are **not** delayed by default. Group-owned doors and doors belonging to other players are delayed.

---

## CarryOn compatibility

When CarryOn is installed and the player is carrying a block in their hands, LockInteract yields entirely and CarryOn handles the interaction on its own.

---

## Configuration

Written to `ModConfig/lockinteract.json` on first launch. The bundled `assets/lockinteract/config/lockinteract.json` shows the defaults.

```json
{
  "Enabled": true,
  "HoldTime": 0.8,
  "ApplyToPersonalLocks": false,
  "BlockCodeAllowList": ["game:door-*"],
  "BlockCodeDenyList": [],
  "ShowProgressOverlay": true,
  "PlayCompletionSound": true,
  "ShowHoldHint": true
}
```

| Option | Default | Description |
|--------|---------|-------------|
| `Enabled` | `true` | Master switch. |
| `HoldTime` | `0.8` | Hold duration in seconds. |
| `ApplyToPersonalLocks` | `false` | When `false`, blocks you personally own open instantly. When `true`, the delay applies even to your own locks. |
| `BlockCodeAllowList` | `["game:door-*"]` | Only locked blocks whose code matches an entry are delayed. Empty means all locked blocks. Supports `*` wildcards. |
| `BlockCodeDenyList` | `[]` | Locked blocks matching an entry are never delayed. Takes precedence over the allow-list. Supports `*` wildcards. |
| `ShowProgressOverlay` | `true` | Show the circular progress ring. |
| `PlayCompletionSound` | `true` | Play a sound on completion. |
| `ShowHoldHint` | `true` | Show the "Hold to open…" label. |

### Example: restrict all locked containers and doors

```json
"BlockCodeAllowList": [
  "game:door-*",
  "game:trapdoor-*",
  "game:fence-gate-*",
  "game:chest-*",
  "game:crate-*"
]
```

### Example: exclude a specific door type

```json
"BlockCodeDenyList": ["game:door-plank-*"]
```

---

## Server deployment

Place the mod in your server's `Mods/` folder. With `requiredOnClient: true` (default), clients without the mod are refused connection. Change to `requiredOnClient: false` in `modinfo.json` for automatic client download on join.

The mod is client-side only for the UI and input handling. All actual access control remains server-enforced.

---

## Building from source

Copy `localSettings.props.template` to `localSettings.props` and set `GameDirectory` to your Vintage Story path.

```
dotnet build LockInteract_1.21.csproj -c Release
```

Output zip is written to `Releases/`.
