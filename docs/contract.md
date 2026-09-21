# The live file (contract, schema 1)

The plugin writes one JSON file. Anything that wants live data reads it. Still a **draft** until module 6, but the
dashboard in this repo reads exactly this.

## Where and how

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\live.json`
- Written once a second while a world is loaded. Written to a temporary file and then moved over `live.json`, so a
  reader never sees half a file.
- On the main menu the file is rewritten **once** with `"inWorld": false` and then left alone; the same is written if the
  game quits while in a world. So `updatedAt` going old means one of two things: the game closed, or it is sitting on
  the menu. A reader tells them apart by checking whether the game process is running (the dashboard does).
- Each section (`player`, `planet`, `vehicle`, and the inventories inside them) is read on its own. A section the
  plugin could not read is `null`; the rest of the file is still good. Readers must cope with any of them missing.
- Numbers the game gives as NaN are written as `null` (NaN is not valid JSON). Readers should treat null as unknown.

## Shape

```json
{
  "schemaVersion": 1,
  "pluginVersion": "0.2.0",
  "gameVersion": "2.103",
  "updatedAt": "2026-09-21T12:00:00Z",
  "inWorld": true,
  "planetId": "Prime",
  "player": {
    "name": "dkolln",
    "position": { "x": 0, "y": 0, "z": 0 },
    "yawDegrees": 0,
    "vitals":    { "oxygen": 370, "health": 70, "thirst": 60, "toxic": 0 },
    "vitalsMax": { "oxygen": 370, "health": 100, "thirst": 100, "toxic": 100 },
    "backpack":  { "size": 320, "items": [ { "id": "Iron", "name": "Iron", "count": 26 } ] },
    "equipment": { "size": 12,  "items": [ { "id": "Jetpack3", "name": "Jetpack T3", "count": 1 } ] }
  },
  "planet": {
    "units": {
      "oxygen": { "value": 0, "increasePerSec": 0, "decreasePerSec": 0 },
      "heat": {}, "pressure": {}, "plants": {}, "insects": {}, "animals": {},
      "biomass": {}, "terraformation": {}, "purification": {}, "energy": {}
    },
    "power": {
      "producedKw": 0,
      "usedKw": 0,
      "generators": [ { "id": "EnergyGenerator2", "count": 24, "kw": 1720 } ]
    },
    "rockets": { "heat": { "count": 3, "multiplier": 30 } }
  },
  "vehicle": {
    "position": { "x": 0, "y": 0, "z": 0 },
    "yawDegrees": 0,
    "trunk": { "size": 200, "items": [] },
    "gear":  { "size": 8,   "items": [] }
  }
}
```

When `inWorld` is false the file has only the first five fields (through `inWorld`).
`vehicle` is `null` when the player has no vehicle yet. `vehicle.position` and `vehicle.yawDegrees` are `null` while the
vehicle is stowed (pocket) or in a portal, because it then has no place in the world.

### Fields

| Field | Meaning |
|---|---|
| `updatedAt` | UTC time the plugin wrote the file |
| `planetId` | The game's planet id (for example `Prime`) |
| `player.position` | The player's raw world position (Unity units, y is up) |
| `player.yawDegrees` | The body's yaw exactly as Unity reports it: 0 is straight along +Z, increasing clockwise seen from above. **Not** a compass heading; see below |
| `player.vitals` / `vitalsMax` | The game's gauge values and their tops, unconverted. `vitalsMax` values are `null` if they could not be read |
| `*.items` | Contents grouped by kind: `id` is the game's group id, `name` its localized display name, `count` how many. `size` is the number of slots |
| `planet.units.<stat>` | The game's own world unit: `value` is the running total, `increasePerSec` what is being gained per second, `decreasePerSec` what is being lost (a negative number). Rates already include every machine, optimizer and rocket |
| `planet.units.energy` | The same numbers for power, in kW: increase is produced, decrease is used |
| `planet.power` | Produced and used in kW, and each kind of generator with its count and current combined output (optimizer boosts included) |
| `planet.rockets.<stat>` | Present only for stats that have launched rockets: how many, and the multiplier the game applies (a Tier 1 rocket is 10, so three Heat rockets are 30) |
| `vehicle.trunk` / `gear` | The truck's storage and its equipped modules |

## Rules

- Positions and angles are the game's raw values. Compass conventions belong to the reader. The dashboard, like
  RRSOS-PCC, treats north as world +X and east as world -Z (measured in the game), so a compass heading is
  `yawDegrees` minus 90.
- A thing with no position in the world is `null`, never `0,0,0`.
- Fields are only ever added within a schema version; anything removed or changed bumps `schemaVersion`.
- Readers must ignore fields they do not know.

## Changes

- **Schema 1** (plugin 0.2.0): the string `planet` became `planetId` (it clashed with the `planet` object);
  added `vitalsMax`, `backpack`, `equipment`, `planet`, `vehicle`.
- **Schema 0** (plugin 0.1.0): position, yaw and vitals only.
