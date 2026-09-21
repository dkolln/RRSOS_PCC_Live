# The live file (contract, draft)

The plugin writes one JSON file. Anything that wants live data reads it. **Draft**: fields change until module 6.

## Where and how

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\live.json`
- Written once a second while a world is loaded. Written to a temporary file and then moved over `live.json`, so a
  reader never sees half a file.
- When the player is not in a world (main menu), the file is rewritten once with `"inWorld": false` and then left
  alone, so a reader can tell "in the menu or game closed" from "file never existed". A reader should also look at
  `updatedAt`: if it is old, the game is not running.

## Shape (v0, as written by plugin 0.1.0)

```json
{
  "schemaVersion": 0,
  "pluginVersion": "0.1.0",
  "gameVersion": "2.103",
  "updatedAt": "2026-09-21T12:00:00Z",
  "inWorld": true,
  "planet": "Prime",
  "player": {
    "name": "dkolln",
    "position": { "x": 0, "y": 0, "z": 0 },
    "yawDegrees": 0,
    "vitals": { "oxygen": 0, "health": 0, "thirst": 0, "toxic": 0 }
  }
}
```

When `inWorld` is false the file has only the first five fields.

### Fields

| Field | Meaning |
|---|---|
| `updatedAt` | UTC time the plugin wrote the file |
| `planet` | The game's planet id (for example `Prime`) |
| `player.position` | The player's raw world position (Unity units, y is up) |
| `player.yawDegrees` | The body's yaw exactly as Unity reports it: 0 is straight along +Z, increasing clockwise seen from above. **Not** a compass heading; see below |
| `player.vitals` | The game's gauge values, unconverted. Same numbers the save stores as `playerGaugeOxygen` and so on |

## Planned (not written yet)

`vehicle` (position, or `null`), `drones` (each with a position), and a `planet` block of live values and rates per
stat plus power (`docs/game-notes.md` explains where the game exposes them).

## Rules

- Positions and angles are the game's raw values. Compass conventions belong to the reader: RRSOS-PCC decides that
  compass north is world +X and east is world -Z (`PCMath.ToEastNorth`, measured in the game), so its heading is `yawDegrees` minus 90.
- A thing with no position in the world is `null`, never `0,0,0`.
- Fields are only ever added within a schema version; anything removed or changed bumps `schemaVersion`.
- Readers must ignore fields they do not know.
