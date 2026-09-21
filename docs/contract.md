# The live file (contract, draft)

The plugin writes one JSON file. Anything that wants live data reads it. **Draft**: fields change until module 6.

## Where and how

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\live.json`
- Written about once a second while a world is loaded. Written to a temporary file and then moved over `live.json`,
  so a reader never sees half a file.
- When the player leaves the world, the file is rewritten with `"inWorld": false` (not deleted), so a reader can tell
  "game closed or in the menu" from "file never existed".

## Shape (v0)

```json
{
  "schemaVersion": 0,
  "pluginVersion": "0.0.1",
  "gameVersion": "2.103",
  "updatedAt": "2026-09-21T12:00:00Z",
  "inWorld": true,
  "player": {
    "position": { "x": 0, "y": 0, "z": 0 },
    "headingDegrees": 0,
    "vitals": { "oxygen": 0, "health": 0, "thirst": 0, "toxic": 0 }
  },
  "vehicle": { "position": null },
  "drones": [],
  "rockets": []
}
```

## Rules

- Positions are the game's raw world (x, y, z). Compass conventions belong to the reader (RRSOS-PCC decides north).
- A thing with no position in the world is `null`, never `0,0,0`.
- Fields are only ever added within a schema version; anything removed or changed bumps `schemaVersion`.
- Readers must ignore fields they do not know.
