# LockInteract

Adds a short hold-to-interact delay when opening locked blocks. Instead of a locked door swinging open the instant you right-click, you must hold the interact button for a brief moment — creating a small amount of intentional friction that makes locks feel more meaningful.

Works on all locked, reinforced blocks. Compatible with CarryOn.

---

## How it works

When you right-click a locked block that passes the configured filters:

1. The interact action is suppressed.
2. A circular progress ring appears at the crosshair and a "Hold to open…" label appears below it.
3. If you hold the interact button for the configured duration (default 0.8s), the block opens.
4. If you release early or look away, the action is cancelled.

The delay applies to all players — owners, group members, and strangers alike (configurable).

---

## Default behaviour

Out of the box, the mod only delays **locked doors** (`game:door-*`). This keeps the friction focused on the most meaningful entry point — a locked door — without affecting chests, crates, or other containers.

Players who own a door personally (sole individual lock, no group) are **not** delayed by default. Group-owned doors, doors owned by other players, and doors you've been granted access to are all delayed.

Both of these defaults are configurable.

---

## Configuration

The config file is written to `ModConfig/lockinteract.json` on first launch. Edit it there to customise behaviour. The bundled `assets/lockinteract/config/lockinteract.json` shows the defaults for reference.

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
| `Enabled` | `true` | Master switch. Set to `false` to disable the mod entirely. |
| `HoldTime` | `0.8` | Hold duration in seconds before the interaction fires. |
| `ApplyToPersonalLocks` | `false` | When `false`, blocks you personally own (sole individual lock, no group) open instantly. When `true`, the delay applies even to your own locks. |
| `BlockCodeAllowList` | `["game:door-*"]` | If non-empty, only locked blocks whose code matches an entry are delayed. Empty means all locked blocks are affected. Supports `*` wildcards. |
| `BlockCodeDenyList` | `[]` | Locked blocks whose code matches an entry are never delayed, even if they would otherwise qualify. Takes precedence over the allow-list. Supports `*` wildcards. |
| `ShowProgressOverlay` | `true` | Show the circular progress ring at the crosshair. |
| `PlayCompletionSound` | `true` | Play a soft sound when the hold completes. |
| `ShowHoldHint` | `true` | Show the "Hold to open…" label below the crosshair. |

### Block code examples

```json
"BlockCodeAllowList": [
  "game:door-*",
  "game:trapdoor-*",
  "game:fence-gate-*"
]
```

```json
"BlockCodeDenyList": [
  "game:door-plank-*"
]
```

---

## Server use

Place the mod in your server's `Mods/` folder. Clients that don't have it installed will receive it automatically on join (`requiredOnClient: false`). The mod is cosmetic and client-side — the server enforces all actual access control as normal. Players without the mod simply get instant interaction, which is the vanilla behaviour.

---

## CarryOn compatibility

When CarryOn is installed and the player is carrying a block in their hands, LockInteract yields entirely. CarryOn manages its own interact delay in that state.

---

## Building from source

Copy `localSettings.props.template` to `localSettings.props` and set `GameDirectory` to your Vintage Story installation path.

```
dotnet build LockInteract_1.21.csproj -c Release
```

The release zip is written to `Releases/`.
