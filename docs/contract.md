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

## Antennas (added in plugin 0.6.0)

**In `live.json`, `antennas`**: each Transmission Antenna (group `ComAntenna`) and where its dish points.

```json
"antennas": [
  { "id": 207872392, "position": { "x": 1102.62, "y": 19.15, "z": 595.25 },
    "heading": 103.1, "degreesPerSecond": 50, "sampledAtMs": 1790186583591 }
]
```

- The dish is a part called `Radar_Base_01`, which the game spins with its `Turn_Move` script about its own vertical
  axis, clockwise from above, at 50 degrees a second (one turn in 7.2 s; found with a probe in the game).
- `heading` is the compass bearing (0 north, 90 east) of that part's **forward** axis at `sampledAtMs` (Unix time,
  milliseconds). The dish itself **faces 90 degrees anticlockwise of it**: calibrated in the game by clicking when the
  dish faced north, which gave -78, i.e. -90 plus the usual early click. `degreesPerSecond` is 0 while the game is
  paused, and while the antenna is out of the game's render range (the game stops turning the dish, and `heading`
  stays where it stopped). The dashboard's sweeps keep turning at the last speed seen through such a pause.
- Its heading at any later moment is `heading + degreesPerSecond × seconds since sampledAtMs`. The dashboard's mini-map
  sweeps follow the nearest antenna this way (`wwwroot/js/radar.js`, with the -90 built in); a button under the maps
  ("Antenna faces N") lets a player re-calibrate, kept per browser.

## Changes

- **Plugin 0.7.0**: `live.json` gains `vehicles`: every truck (group `VehicleTruck`), oldest first, each shaped like
  `vehicle` plus its `id`. `vehicle` stays, as the first truck, for older readers. The dashboard names trucks "Truck 1",
  "Truck 2", ... in that order (the game gives them no names).
- **Plugin 0.6.0**: `live.json` gains `antennas` (see "Antennas").
- **Plugin 0.5.0**: the world file loses `loose` and gains `planetHash`; the boneyard comes from the save (see "The boneyard").
- **World file, schema 1** (plugin 0.3.0): new file `live-world.json` (see above). `live.json` gains a `drones` section (see "Drones").
- **Schema 1** (plugin 0.2.0): the string `planet` became `planetId` (it clashed with the `planet` object);
  added `vitalsMax`, `backpack`, `equipment`, `planet`, `vehicle`.
- **Schema 0** (plugin 0.1.0): position, yaw and vitals only.

## The world file (`live-world.json`, schema 1)

Bases, containers and extractors change slowly and there can be thousands of them, so they are **not** in `live.json`.
The plugin (0.3.0 and later) writes them to a second file beside it. The dashboard watches both.

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\live-world.json`
- Written about every 5 seconds while a world is loaded (the pass itself is spread over several frames, at most about
  2 ms of work each, so it never shows up as a stutter). Written atomically, like `live.json`.
- Out of a world it is written **once** as `{"schemaVersion":1,"pluginVersion":...,"updatedAt":...,"inWorld":false}`.
- Same rules as `live.json`: numbers the game gives as NaN are `null`, fields are only ever added within a schema
  version, readers ignore fields they do not know. An empty list is `[]`, never missing, when the pass ran.
- The plugin reports **raw facts**. Deciding what a base is, naming it and working out which container belongs to
  which base all happen in the dashboard (see "What a base is" below).

```json
{
  "schemaVersion": 1, "pluginVersion": "0.5.0", "updatedAt": "2026-09-21T12:00:05Z", "inWorld": true, "planetId": "Prime",
  "planetHash": -1140328421,
  "scan": { "objectsVisited": 31240, "frames": 9, "workMs": 14.2, "worstFrameMs": 2.1 },
  "pods": [
    { "id": 204266353, "group": "pod", "position": { "x": 803, "y": 35, "z": 613.5 }, "panels": [4, 2, 3, 3, 5, 7] }
  ],
  "signs": [ { "id": 202057317, "position": { "x": 800.38, "y": 37.26, "z": 617.63 }, "text": "Main" } ],
  "containers": [
    { "id": 204236051, "group": "VegetableGrower2", "position": { "x": 772.25, "y": 36.04, "z": 579.75 },
      "items":     [ { "id": "Fertilizer1", "name": "Fertilizer", "count": 4 } ],
      "secondary": [ { "id": "Vegetable3Growable", "name": "Mushroom Plant", "count": 6, "ready": 4 } ] }
  ],
  "extractors": [
    { "id": 205746819, "kind": "ore", "group": "OreExtractor3", "position": { "x": 932, "y": 23, "z": 551 },
      "product": "Titanium", "productName": "Titanium", "size": 8, "count": 8, "productCount": 8, "ready": 0,
      "items": [ { "id": "Titanium", "name": "Titanium", "count": 8 } ] }
  ]
}
```

### Fields

| Field | Meaning |
|---|---|
| `scan` | How much work the last pass took: objects looked at, frames it was spread over, total work and the longest single chunk, in milliseconds. A check that reading the world costs the game nothing; the dashboard does not use it |
| `pods[]` | Every living compartment on this planet (group id starting `pod`, or `EscapePod`). `panels` is what fills each of its sides, in the game's `BuildPanelSubType` numbers: **4 is a door (entrance), 2 a connection (corridor)**, 3 a window, 1 plain wall, 5 to 7 floors. The order is the game's (east, west, north, south, top, bottom). A list, `null` when the game has none for that object |
| `signs[]` | Every placed sign and what it says (`text`). Base names come from signs |
| `containers[]` | Placed objects with storage that hold something, within 120 m of some pod. `items` is the main storage, `secondary` any secondary storage (a grower keeps its plants there). Items are counted by kind. `ready` (only when above zero) counts plants in `secondary` that have finished growing (growth 100) |
| `planetHash` | The game's hash for the planet (0.5.0 and later). Each object in a save carries it as `"planet"`, so the dashboard can keep only this planet's loose items from the save |
| `loose[]` | **Gone in 0.5.0.** Loose items were read live up to 0.4.1, but the counts were unreliable; the dashboard now reads them from the save (see "The boneyard" below) |
| `extractors[]` | Ore, gas, water and algae machines. `kind` is `ore`, `gas`, `water` or `algae`. `product` and `productName` are what an ore or gas extractor is set to produce (null for water and algae). `size` is its slot count, `count` how many items it holds, `productCount` how many of those are the product, `ready` (algae) how many have finished growing. `items` is everything in it, by kind |
| `position` | Raw world position, two decimals. Compass conventions belong to the reader, as in `live.json` |

Everything is limited to the planet the player is on.


### Drones (added in plugin 0.3.0)

**In `live.json`, `drones`** (the ones in the air, read every second):

```json
"drones": {
  "flying": [
    { "id": 206956029, "group": "Drone2", "name": "Drone T2",
      "position": { "x": 800.1, "y": 41, "z": 620.4 }, "yawDegrees": 132.5, "speed": 9.5,
      "state": "ToSupply",
      "cargo": { "size": 4, "items": [ { "id": "Iron", "name": "Iron", "count": 4 } ] },
      "moving": { "id": "Iron", "name": "Iron" },
      "from": { "id": "Container1", "name": "Storage Container", "position": { "x": 806, "y": 35, "z": 617 } },
      "to":   { "id": "VegetableGrower2", "name": "Vegetable Grower T2", "position": { "x": 772.25, "y": 36, "z": 579.75 } } }
  ]
}
```

- `flying` lists every drone on this planet that is a live, active scene object. Drones sitting in a station are not in it (they have no place in the world).
- `state` is the game's task state (`NotAttributed`, `ToSupply`, `ToDemand`, `Loading`, `Unloading`, `Done`), or `Returning` when the drone has no task. `moving`, `from` and `to` are null with no task.
- `speed` is the drone's forward speed; `yawDegrees` is Unity's, as for the player. `cargo` is its own storage, in the same shape as the backpack.
- The list of drones is refreshed every 5 seconds; between refreshes only positions, tasks and cargo are read. `drones` is null if it could not be read.

**In `live-world.json`, `droneStations`** (slow, with the rest of the world):

```json
"droneStations": [
  { "id": 208001, "group": "DroneStation1", "name": "Drone Station", "position": { "x": 790, "y": 35.5, "z": 600 },
    "size": 10, "docked": 3, "items": [ { "id": "Drone2", "name": "Drone T2", "count": 3 } ] }
]
```

`size` is the station's slot count, `docked` how many drones are in it, `items` everything in its storage. Stations are reported separately, so
they are not also in `containers`.

### Building pieces (added in plugin 0.4.0), for the floor plans

**In `live-world.json`, `structures`**: every pod (all shapes), foundation, platform, dome, lab and ladder within 120 m of a pod.

```json
"structures": [
  { "id": 202460591, "group": "pod", "position": { "x": 1120.5, "y": 25.5, "z": 632 }, "yaw": 0,
    "panels": [1, 2, 1, 2, 5, 7],
    "box": { "min": { "x": -4, "y": 0, "z": -4 }, "max": { "x": 4, "y": 6, "z": 4 } },
    "panelBoxes": [ { "type": 1, "sub": 1, "ceiling": false, "min": { "x": -4, "y": 0, "z": 3.9 }, "max": { "x": 4, "y": 6, "z": 4.1 } } ] }
]
```

(The numbers above show the shape only.)

- `yaw` is Unity's, in degrees. `box` is the piece's solid colliders (or, with none, its meshes) measured **in the piece's
  own frame**: metres from `position`, before turning by `yaw`. `panelBoxes` does the same for each of its `Panel`s, in
  the same order as `panels`. `type` is 1 wall, 2 floor, 3 angled floor; `sub` uses the same codes as `panels`
  (the game's `BuildPanelSubType`: 1 wall, 2 corridor, 3 glass, 4 door, 5 floor with light, 6 glass floor, 7 plain floor,
  8 lab floor, 9 lab wall, 10 floor without light, 11 inside wall, 13 aquarium wall). Both are null when the piece has
  no scene object to measure (and always in files made from a save by `tools\save-to-world.ps1`).
- A piece belongs to the nearest base within 100 m, like a container. The dashboard's `FloorPlan` uses the measured
  box when there is one, and a table of sizes otherwise (the plan then says "approximate"). From a save, a plain pod's
  first four `panels` are its sides in the order +Z, -Z, +X, -X (worked out from which sides of neighbouring pods are
  joined by corridors in two saves).
- `deckBox` (plugin 0.4.1) is the piece's largest flat slab of collider plus any slabs level with it; for a platform
  that is its deck, and its top is the deck's height. The full `box` of a launch platform is far bigger than the deck.
- Floors are found from heights: a foundation counts at its top (2 m above its position when not measured), a launch,
  trade or vehicle platform at its deck (5 m above its position when not measured), a ladder on the floor of the room it
  stands in, anything else at its position; a gap of more than 2.5 m starts a new floor.

### What a base is (the dashboard's rules, ported from RRSOS-PCC)

- A **base** is a pod (group `pod` or `EscapePod`) with a **door** in its `panels` (a 4). With a **connection** (a 2) as
  well it is shown as a *Base*; with only a door it is an *Outpost*. Larger modules (`Pod4x`, `Pod9xC`) are not bases.
- A base's name is the text of the nearest sign within 6 m; else the name saved for its pod id; else the next unused
  name from a list (Greek names for bases, NATO letters for outposts), which is then saved. Names are kept in the
  dashboard's own `basedata.json` beside the live files, keyed by the game's id for the pod.
- A container or a loose item belongs to the nearest base within **100 m** (flat distance). Machines and building parts
  (by RRSOS-PCC's table of item types) are not listed as stored items. The **boneyard** (loose items) is **loose raw material only**: the types Ore, Alloy, Quartz and Rod in that table
  (iron, cobalt, titanium, silicon, magnesium, iridium, aluminium, uranium, sulfur, obsidian, zeolite, osmium, ice, super alloy, the quartzes and the rods).
  Plants, drones, vehicles and the rest that are lying about are not shown.

### The boneyard (from the save, dashboard 0.5.0 and later)

Loose items are **not** read from the running game. The dashboard's `SaveLooseService` does what RRSOS-PCC does:

- Every **10 seconds** it looks at the save folder (`SaveSettings:SavePath`, default `...\LocalLow\MijuGames\Planet Crafter`).
  The newest `.json` there, leaving out the game's `Backup.json` copy, is the save being played.
- When that file has been written since the last look (and not in the last 2 seconds, so it is not read half-written), it
  reads every record with a `"pos"`. An item in a container, a backpack or a vehicle has no position in the save, so what is
  left is what lies in the world (plus buildings and machines, which the raw-material filter above leaves out).
- Only records whose `"planet"` matches the world file's `planetHash` are kept (all of them when either is missing).
- The boneyard is therefore as fresh as the **last save**, autosave included. Its title on the Base card says when that was:
  "Boneyard (save 12:14)". The save is only ever read.
- **Ready to harvest** is the crops with `ready` in the planting tray of a grower (`VegetableGrower*`, `Farm1`).
