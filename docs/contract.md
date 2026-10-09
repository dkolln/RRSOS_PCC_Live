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
      "generators": [ { "id": "EnergyGenerator2", "name": "Solar Panel T1", "count": 24, "kw": 1720 } ],
      "consumers": [ { "id": "OreExtractor2", "name": "Ore Extractor", "count": 8, "kw": 400 } ]
    },
    "rockets": { "heat": { "count": 3, "multiplier": 30 } },
    "phases": [
      { "id": "Oxygen1", "name": "Lakes", "unit": "Terraformation", "startValue": 8000, "complete": true },
      { "id": "Plants1", "name": "Plants", "unit": "Plants", "startValue": 12000, "complete": false }
    ]
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
| `planet.power` | Produced and used in kW, and each kind of generator (`generators`) and of machine drawing power (`consumers`, plugin 0.16.0) with its name, count and current combined kW (optimizer boosts included; a consumer's kW is positive) |
| `planet.rockets.<stat>` | Present only for stats that have launched rockets: how many, and the multiplier the game applies (a Tier 1 rocket is 10, so three Heat rockets are 30) |
| `planet.units.purification` | Same shape as the other stats. The game parks `value` at -1 on a planet that does not need purification |
| `planet.toxicity` | The game's Toxicity screen (plugin 0.14.0): `cleanedObjects` of `totalObjects` toxic goo objects cleaned, `cleanedAreas` of `totalAreas` toxic areas fully clean. Summed from the game's toxic areas handler; null when it is not there |
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

## The blueprint list (`blueprints.json`, added in plugin 0.8.0)

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\blueprints.json`. Written **once per game session** (when the game's group data is ready), not
  on every tick. Read by the dashboard's Cheats → Base Building ("Equipment and tokens" recipe).
- Shape: `{ "chip": "BlueprintT1", "tiers": [[ids] x10], "messages": [ids], "loot": [ids] }`.
  - `chip`: the one blueprint item gId the game knows.
  - `tiers`: the building groups unlocked by a blueprint chip, per tier (`unlockingData.tier1..10GroupToUnlock`).
  - `messages`: groups unlocked by a message (toxicity items), not by chips.
  - `loot`: groups whose deconstruction drops a chip linked to that group (`Group.GetLootRecipeOnDeconstruct`).
- A chip is `{"id","gId":"BlueprintT1","liGrps":"<group>"}` in the save; `liGrps` names the building it unlocks.

## The planet name catalog (`planets.json`, added in plugin 0.10.3)

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\planets.json`. A save (and `live-world.json`'s `planetHash`) only ever carries a planet's
  hash (the game's own `GetStableHashCode()` of its id: "Prime" → -1140328421, "Humble" → -486276833), never its name, so
  this is the only place the dashboard can learn one. Written whenever a hash is seen for the first time, or under a
  different name than before (should not happen); grows one entry per planet the owner actually visits, in whatever
  session first visits it with 0.10.3 or later. A hash the owner has not visited yet with this plugin version has no entry,
  and the dashboard just shows the number until it does.
- Shape: `{ "<hash>": "<planetId>", ... }`, e.g. `{"-1140328421":"Prime","-486276833":"Humble"}`.
- Read by `PlanetNames` (dashboard), which labels a beacon's or a drone-network planet's hash wherever more than one is in
  play: the Base Building and Factory beacon pickers, and the Drone Network tab's planet selector (below).

## Terraformation phases (`planet.phases`, added in plugin 0.10.5)

- Every planet has its own named terraformation milestones — the game's own "progress" screen ("Lakes", "Animals",
  "Complete Transformation", ...; not every one lines up with a planet gauge) — read straight from `PlanetData`'s own
  `allTerraStages` (`TerraformStage`), the same list the game's own `TerraformStagesHandler` uses. `name` is the game's own
  localized text (`Readable.GetTerraformStageName`); `unit` is the gauge the milestone is thematically closest to (or
  `Terraformation` for one with none, like Lakes); `startValue` is the total Terraformation (TI) it unlocks at; `complete`
  is `startValue <= the current Terraformation value`, the game's own test. Written every tick, in order of `startValue`,
  **for whichever planet the player is on** — each planet keeps its own list and thresholds.
- The dashboard's **Phases** card (`PhasesCard.razor`, between Planet and Power on the Home page) draws one pill per
  phase: solid green once `complete`, flashing for the single next one still incomplete (the "in progress" one), plain
  for the rest. `LiveFileService` also speaks through `SpeechService` as these change, independent of any
  browser tab being open: "Phase *name* is complete" the moment one finishes, "Phase *name* now in progress" as the next
  one starts, and once, when the very last one finishes, "Terraformation is complete congratulations planet crafter."
  Keeps score per planet (by `planetId`) so switching planets never looks like an un-completion, and the first reading of
  a planet's phases is never itself announced (just the baseline to compare the next one against).

## Planet-scoping (plugin 0.10.2 and 0.10.3)

Two things used to pool every planet in a save together, which only mattered once the owner had more than one:

- **`planet`'s power and rockets** (plugin 0.10.2): `PlanetReader`'s generator list and rocket counts now only count objects
  whose own `"planet"` matches the current world's (the game does the same for the totals themselves); before, a second
  planet's fusion generators and rocket multipliers leaked into the first one's numbers.
- **Beacons and the Drone Network** (plugin 0.10.3 adds `planets.json`; the filtering itself needed no plugin change, since
  a save's object records already carry `"planet"`): `BuildBeacon.PlanetHash` and `DroneNetworkEngine`'s `Rec.Planet` read
  that field. The Base Building and Factory beacon lists show the planet name next to a beacon's own name when it is
  known (`PlanetNames`), and `BaseBuilding.razor`'s "near base" line no longer matches a beacon against a base on another
  planet by coincidence of raw coordinates. The Drone Network tab offers a **Planet** picker whenever the save has more
  than one (`DroneNetworkEngine.PlanetsInSave`); picking one scopes every producer, container and demanding machine to
  it (`DroneNetworkEngine.Apply`'s `planetHash` parameter). An object record with no `"planet"` of its own (should not
  happen for anything these two look at) is never hidden by either filter, on either planet, rather than guessed at.

## The recipe list (`recipes.json`, added in plugin 0.9.0)

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\recipes.json`. Written **once per game session** (when a world has loaded), not on every tick. Meant as the one
  place that says how to make an object, for any tool (the planned Production planner reads it).
- Shape: `{ "schema": 1, "recipes": [ ... ], "machines": [ ... ] }`.
- A **recipe** is one object (item or building) that has ingredients:
  `{ "id": "Rod-osmium", "kind": "item", "ingredients": { "Osmium": 1, "Alloy": 1 }, "category": "...", "craftableIn": ["CraftStationT2"], "hideInCrafter": false, "unlock": { "unit": "...", "value": 0 } }`.
  - `ingredients`: group id to how many of it (the game lists an ingredient twice for two).
  - `kind`: `item`, `building` or `other`. `category` and `craftableIn` are only on items.
  - `craftableIn`: the game's crafting places for the item (`CraftStationT1..T3`, `CraftOvenT1`, `CraftQuartzT1`, `CraftBioLab`, `CraftIncubatorT1`,
    `CraftGeneticT1`, `CraftDroneT1`, `CraftToxicRefinementT1`, `CraftRocket`, `CraftVehicleT1`, `CraftDeparturePlatform`). An autocrafter can make an item
    only if it is craftable in one of the first eight of those minus the incubator and genetic ones (see `ActionGroupSelectorAutoCrafter`).
  - `unlock`: the terraformation unit and value the game unlocks it at (`Null` means not from the start: a blueprint or a story event).
- A **machine** is a building that crafts: `{ "id": "AutoCrafter1", "crafts": "CraftStationT1", "craftTime": 1.5, "autocrafter": { "range": 0, "craftEverySec": 0 } }`.
  `range` is the autocrafter's reach in metres (a 3D sphere: height counts), `craftEverySec` how often it tries to craft. `convertsRecipe` marks the machines
  that finish a recipe by growth (the incubator).
- Read by the game's own groups (`GroupsHandler.GetAllGroups()`, each `GetRecipe()`), so it covers every planet's items the running game has.

## The unlock thresholds (`unlocks.json`, added in plugin 0.15.0)

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\unlocks.json`. Written **once per game session** (when a world has loaded). One entry for every object the game knows, items and
  buildings alike, so a reader can tell what a save has unlocked without the game running (the dashboard compares it with the world unit levels and `unlockedGroups`
  that every save carries). The game's thresholds live in its assets, so reading the running game is the only way to know them.
- Shape: `{ "schema": 1, "groups": [ { "id": "Farm1", "name": "Vegetable Farm", "kind": "building", "unit": "Oxygen", "value": 500000, "planets": [], "viaBlueprint": false, "stage": "...", "usage": "...", "unlockedNow": true } ] }`.
  - `unit` and `value`: the world unit (`Oxygen`, `Heat`, `Pressure`, `Biomass`, `Plants`, `Insects`, `Animals`, `Terraformation`, `SystemTerraformation`, `Purification`) and the level that
    unlocks it. When the game unlocks on a terraform stage, `value` is the stage's start value and `stage` is the stage's id. `Terraformation` at 0 means there from the start; `Null` means
    not on a world unit at all (a blueprint chip, a message or the story).
  - `planets`: the planet ids it only unlocks on (empty means any planet). `usage` is the game's planet usage type (whether it can be used on every planet).
  - `viaBlueprint`: it has to be unlocked with a blueprint chip or a message (the tier lists are in `blueprints.json`).
  - `unlockedNow`: whether the world the plugin is in has it unlocked right now (the game's own check).
  - `inventory` and `secondary`: the slots of its own inventory and of its secondary inventories (only when it has any), so a reader can write a new one into a save.
  - `category` only on items.

## The pinned recipes (`pins.json`, added in plugin 0.11.0)

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\pins.json`. The recipes pinned to the top right of the screen with the Blueprint pinning microchip. Written when the pins
  change (the plugin looks twice a second), so it is not on a timer. It is not written on the main menu, where there is no pin list: the last pins stay.
- Shape: `{ "schema": 1, "pins": [ { "id": "OreExtractor3", "name": "T3 Ore Extractor", "ingredients": [ { "id": "Rod-iridium", "name": "Iridium Rod", "count": 1 } ] } ] }`.
  `name`s are the game's own display names, so a reader needs no table; the same ingredient listed twice by the game is one entry with a `count` of 2.
- The game keeps the pins in a private list of its pin canvas (`CanvasPinedRecipes._groupsAdded`), which the plugin reads by reflection; it only reads. The game
  allows only a few pins at a time and drops the oldest, so the dashboard keeps its own list of everything that was ever pinned (`shopping-list.json`, the Shopping
  list card). A pin already showing when the dashboard starts is not added, so a restart adds nothing twice.

## The item icons (`icons\`, added in plugin 0.12.0)

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\icons\<group id>.png`, one file for every item and building that has an icon in the game (about 670).
- Written **once per install** (when a world has loaded, a few at a time so the game does not stutter); a file already there is left alone, so a group a game update
  adds is picked up and nothing is written twice. The game holds an icon as a sprite cut out of a shared texture atlas that code may not be allowed to read, so each is
  copied through a render texture into a PNG of the sprite's own size (at most 256 px).
- The dashboard serves them at `/icons/<group id>.png` (a missing file is a 404) and shows each beside its name; nothing breaks without them. File names are the group
  id, with any character a file name cannot hold replaced by `_`.

## What makes each planet stat (`terraformers.json`, added in plugin 0.13.0)

- Path: `%LOCALAPPDATA%\RRSOS-PCC-Live\terraformers.json`. Rewritten about every 3 seconds while a world is loaded (and only when its content changed); not written on the main menu,
  where the last reading stays.
- Shape: `{ "schema": 1, "planetHash": N, "units": { "oxygen": [ { "id": "Biodome2", "name": "Biodome", "count": 3, "each": 40000000, "total": 126000000, "unlocked": true } ], "heat": [...], ... } }`.
  The units are `oxygen`, `heat`, `pressure`, `plants`, `insects`, `animals` and `purification`.
- One entry for every building the game has that makes the stat, built or not (`GroupConstructible.GetGroupUnitGeneration(unit) > 0`), plus anything built that makes it:
  - `each`: what one makes per second, from the game's building data.
  - `count`, `active` and `total`: how many are built on **this planet**, how many of those are making some of the stat right now, and what they make together, the sum of each machine's own live
    figure (`WorldObject.GetUnitGeneration`, optimizer boosts included; the same figure the power card uses). Rockets and other multipliers are not in it. `active` can be less than `count`
    because a machine that makes its stat through what it holds (`WorldUnitGenerationViaInventory`: planters, spreaders and the like) reports 0 while it is empty, though it is built.
  - `unlocked`: the game's own test for showing a building in its menus (`Group.GetUnlockingInfos().GetIsUnlocked(true)` or `Group.GetIsGloballyUnlocked()`), true whenever one is built.
- The dashboard shows it when a planet gauge is clicked: strongest first, the not-unlocked ones greyed.

## Changes

- **Plugin 0.17.0**: `extractors[]` also lists harvesting robots (kind `harvester`, with the item they are set to), and each entry gains `supply` (the group ids its drone settings supply). The Factory tab counts an ore, gas or water source as supplied only when its product is on that list.
- **Plugin 0.16.0**: `planet.power` gains `consumers[]` (what draws power, by kind) and a `name` on each generator and consumer. The dashboard shows both when the power gauge is clicked.
- **Plugin 0.14.0**: new `planet.toxicity` in `live.json`, and Toxic Water Collectors in `extractors[]` (kind `toxicwater`). See above.
- **Plugin 0.15.0**: new file `unlocks.json` (see above).
- **Plugin 0.13.0**: new file `terraformers.json` (see above). Nothing in the other files changes.
- **Plugin 0.12.0**: the item icons, as PNG files (see above). Nothing in `live.json` or `live-world.json` changes.
- **Plugin 0.11.0**: new file `pins.json` (see above).
- **Plugin 0.9.0**: new file `recipes.json` (see above).
- **Plugin 0.8.0**: new file `blueprints.json` (see above).
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
      "label": null, "labelName": null,
      "items":     [ { "id": "Fertilizer1", "name": "Fertilizer", "count": 4 } ],
      "secondary": [ { "id": "Vegetable3Growable", "name": "Mushroom Plant", "count": 6, "ready": 4 } ] },
    { "id": 204780410, "group": "AutoCrafter1", "position": { "x": 716.75, "y": 45.52, "z": 673.25 },
      "label": "Rod-osmium", "labelName": "Osmium Rod",
      "items": [], "secondary": [] }
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
| `containers[]` | Placed objects with storage that hold something, within 220 m of some pod (widened from 120 m in plugin 0.10.0, for a large factory's farthest platforms). `items` is the main storage, `secondary` any secondary storage (a grower keeps its plants there). Items are counted by kind. `ready` (only when above zero) counts plants in `secondary` that have finished growing (growth 100). `label`/`labelName` (plugin 0.10.0 and later) are what the container is set for: a warehouse chest's demand item, or an autocrafter's chosen recipe (the same linked-group the game uses for an ore/gas extractor's product); null when nothing is picked |
| `planetHash` | The game's hash for the planet (0.5.0 and later). Each object in a save carries it as `"planet"`, so the dashboard can keep only this planet's loose items from the save |
| `loose[]` | **Gone in 0.5.0.** Loose items were read live up to 0.4.1, but the counts were unreliable; the dashboard now reads them from the save (see "The boneyard" below) |
| `extractors[]` | Ore, gas, water, toxic water and algae machines. `kind` is `ore`, `gas`, `water`, `toxicwater` (the Toxic Water Collector, plugin 0.14.0), `algae` or `harvester` (a harvesting robot, plugin 0.17.0). `product` and `productName` are what an ore or gas extractor or a harvesting robot is set to produce (null for water and algae). `size` is its slot count, `count` how many items it holds, `productCount` how many of those are the product, `ready` (algae) how many have finished growing. `items` is everything in it, by kind. `supply` (plugin 0.17.0) is the list of group ids its drone settings say to supply, so a machine feeds the factory only when its product is on it; `[]` when none, null when not read (an older plugin, or the algae generator) |
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

**In `live-world.json`, `structures`**: every pod (all shapes), foundation, platform, dome, lab and ladder within 220 m of a pod.

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
- A piece belongs to the nearest base within 200 m, like a container. The dashboard's `FloorPlan` uses the measured
  box when there is one, and a table of sizes otherwise (the plan then says "approximate"). From a save, a plain pod's
  first four `panels` are its sides in the order +Z, -Z, +X, -X (worked out from which sides of neighbouring pods are
  joined by corridors in two saves).
- **The departure platform** (`DeparturePlatform`, plugin 0.10.1): the game keeps it out of its "constructed" set, so the
  scan also looks through all placed objects for any other group with "Platform" in its id (not `Blueprint...`). Its box is
  exactly its deck's bounds (35.8 x 23.8 m). `landing` (0.10.1, only on this piece) is where the module that comes back lands:
  `position` and `forward` in the piece's own frame, from the game's own `GetLandingTransform()`; null for everything else.
  The floor plan hand-maps the rest (see `FloorPlan.DeparturePlatformLevels`).
- **`rocket` (plugin 0.10.4)**, on a trade platform or an interplanetary exchange platform only: the unmanned round-trip
  rocket it carries, read from the game's own `MachineRocketBackAndForth` (the base class both share): `{ "kind": "trade" |
  "interplanetary", "onSite": bool, "returnsInSec": number }`. `onSite` false to true is an arrival, true to false a
  departure; `returnsInSec` is 0 while docked. Null for every other piece. The dashboard's `WorldFileService` watches this
  across readings and speaks "Trade Rocket Departing"/"Arriving" (or "Interplanetary Rocket ...") through `SpeechService`
  when it changes, from the background, whether or not a browser tab is open.
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
- A container or a loose item belongs to the nearest base within **200 m** (flat distance; widened from 100 m 2026-09-27, for a large factory's farthest platforms). Machines and building parts
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
