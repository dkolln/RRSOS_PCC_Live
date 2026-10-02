# RRSOS PCC Live

A live view of **The Planet Crafter**: a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that reports the running
game's state to two small JSON files, and a local dashboard that shows it on one page in your browser. The plugin only
ever observes the game.

It is a separate project from [RRSOS-PCC](https://github.com/dkolln/RRSOS-PCC), the companion dashboard. That app works
only from the game's **save file**, which the game writes only when it saves, so nothing in it can be truly live. This
project reads the running game instead (and uses the save only for the one thing the live game cannot tell it reliably:
loose items on the ground). Neither project depends on the other to build.

```
Planet Crafter  --(plugin, every second)----> live.json        --(watches)-->  Dashboard (browser)
                --(plugin, every 5 seconds)-> live-world.json  --(watches)-->
                --(plugin, when you pin)-----> pins.json        --(watches)-->
                --(plugin, once per session)-> recipes.json, blueprints.json, icons\*.png
                                              %LOCALAPPDATA%\RRSOS-PCC-Live
newest save file (every 10 s, when it changes) ------------------------------> Dashboard (boneyard only)
```

## What the dashboard shows

One page, laid out for a 2560 x 1440 monitor, three columns.

**Player (left)**
- Compass, altimeter, oxygen, health, thirst and toxicity gauges, backpack and worn gear.
- **Four mini maps** with you in the middle and north up: **Bases**, **Drones**, **Vehicles** and **Extractors**. Each
  has zoom buttons, hover names, and a radar sweep that turns with the Transmission Antenna's dish (a button lets you
  line it up with the dish you see). Click a map to open its card in the right column.

**Planet (middle)**
- Six terraformation dials (oxygen, heat, pressure, plants, insects, animals) with live per-second rates and the rocket
  count and multiplier under each, the total TI, a **Phases** row and the power dial with one icon per generator. Under
  it, a notes card.
- **Phases**: the current planet's own named terraformation milestones (the game's "progress" screen — Lakes, Animals,
  Complete Transformation, ...; not every one lines up with a gauge), each planet with its own list: solid once
  complete, flashing for the one in progress, plain for the rest, with a live percentage toward the next one.

**Shopping list (right, under the detail card)**: every recipe you pin in the game (the Blueprint pinning microchip, top
right of the screen) lands here and **stays after the pin is cleared**, so the game's few pin slots are no limit. Each
entry has a quantity (− / +) and a ✕; below them, the **totals** of every ingredient across the whole list. A total turns
**yellow** when your backpack holds some of it and **green** when it holds all of it. An **ids** switch shows game ids
instead of names where space is tight. Pinning something already on the list does not add it twice. The list is kept in
`shopping-list.json`; the dashboard has to be running while you pin.

**Icons**: the game's own icon for every item and building sits beside its name in the backpack, gear, trunk, base and
extractor lists, the shopping list, the truck tiles, and on the power card (the machine itself, in place of the little
drawn windmill, atom and so on; the drawing is still used when an icon is missing). On the floor plan a container shows
the icon of the product it is set for, and the warehouse preview on the Cheats page draws the item on each chest. The
plugin writes them out of the running game once (`icons\<group id>.png`, plugin 0.12.0), so nothing is bundled or copied
from anywhere else.

**Detail card (right)**: shows whichever map you last clicked.
- **Base**: what the base you are at (or picked on the map) holds: stored items, crops ready to harvest, the
  **boneyard** (loose ore, alloy, quartz and rods lying around, read from the last save), the **floor plan**, and, once
  that base has a factory, a **Factory card**: which products its autocrafters are short of ingredients for, click a
  line to see which crafters.
- **Floor plan**: the base drawn from above, one floor at a time (▲/▼, or it follows the floor you stand on): pods, walls,
  windows and doors, foundations, platforms (launch, vehicle, trade, **the departure platform**), domes, labs, ladders,
  containers and you.
- **Drones**: flying drones with their tasks and cargo, and every drone station with what is docked in it. The map opens
  itself the moment a drone takes off and closes back to Base once every one has landed; a 📌 pin stops that when you
  want to stay put (picking the drone view by hand pins it, picking anything else un-pins it).
- **Vehicles**: every truck (Truck 1, 2, ...), where it is or that it is stowed, its trunk and modules.
- **Extractors**: every ore, gas, water and algae machine, grouped, with fill level, position, and distance and
  direction from you.

**Also**
- **Spoken alerts**, through a Windows voice (a picker next to the volume slider lists every one installed; your pick
  and the volume are both remembered): low oxygen, health and thirst; a trade or interplanetary-exchange rocket
  departing or arriving; a terraformation phase completing or the next one starting, and once per planet,
  "Terraformation is complete". Two landing close together are both heard in full (they queue, with a short pause,
  rather than a newer one cutting an older one off).
- **Multiple planets**: beacons, the Drone Network picker and Resupply all show which planet is which (a `planets.json`
  cache learns each one's own name as you visit it) and scope to the one picked, so a beacon or a container label
  shared by two planets is never pooled or mixed up with the other's.
- **LAUNCH PC** starts the game through Steam. It is disabled while the game runs.
- **CHEATS** (only while the game is not in a world): five tabs that edit a save file on disk, see the principles
  below and the next section.

Lists (backpack, gear, trunks, stored items) can be folded by clicking their titles.

## Cheats: editing a save

Every Cheats tab works on a save file, only while the game is at the main menu or closed. Each edit makes a byte-exact
backup first, writes atomically, and is refused unless undoing it would give back the original file exactly.

- **Resupply**: a configurable list of items to top up in your inventories. With a save that has labelled containers
  on more than one planet, a **Planet** picker scopes RESUPPLY to the one chosen (does not depend on a warehouse beacon
  — a planet with none named "All" still works).
- **Drone Network**: sets which containers supply and which demand, so drones move things without hand setup. A
  container is either a demand sink or a supply source; producers supply what they hold, extractors and ecosystems
  supply everything. Pick which producers and consumers to switch on, and choose the item a container demands. Same
  **Planet** picker as Resupply when more than one planet has producers or containers. Each producer says what it is
  set to supply now ("Supplies everything (206 items)", or the names), and where a pending change names items it shows
  each one's icon and name ("→ will supply 🍯 Honey"). A producer set to something other than what it makes has **Fix**,
  which sets it to the usual items for its kind, and **Choose…**, which offers each item that kind makes, one at a time or
  all together; the item box also takes several ids separated by commas (`honey,Bee1Larvae`). The usual items come from
  `producer-supply-defaults.json` beside the dashboard's other files, written once with `Beehive2` = honey and
  Bee1Larvae (a hive holds only what it has made so far, so what it holds is no guide to what it makes) and yours to
  edit; a kind with no entry gets everything it is seen holding.
- **Travel**: moves the player to a spot on any planet the save knows about (a `x,y,z` you type, or that planet's own
  warehouse beacon when you leave it empty). Same backup-first write as the others.
- **Base Building**: builds a whole row of storage.
  1. Place a foundation with a **beacon** whose text names the job (for example "Fish"); the way the beacon faces is
     the way the row grows.
  2. Build one platform of chests (Container1, 2 or 3) as a sample and **capture** it as a template.
  3. Pick the beacon, a **group** and a template. The tab shows the plan (an SVG preview, the labels, anything in the
     way) and, on **BUILD ROW**, writes the platforms and chests into the save. A beacon's card shows which planet it
     is on (once `planets.json` has learned its name) — handy when two planets each have a beacon with the same name.

  Each chest is labelled and filtered with the next item of the group, nearest platform first and left chest before
  right. Options: set every chest to demand its item, and fill it. There are 18 groups: fish eggs, frog eggs,
  butterfly larvae, tree seeds, plant seeds, vegetables, larvae, petri dishes, quartz crystals, rods, ores, crafting
  materials, fuses, food and cooking, toxic and purification, drones, essentials, and **Equipment and tokens**.
  That last one stocks one chest each with one of every spacesuit, personal item and vehicle item, a **Blueprints**
  chest with one linked chip for every building you have not unlocked yet (split over several chests when the
  template is small), and a box of terra tokens. It needs a Container2 or Container3 template. The blueprint list
  comes from the game itself: plugin 0.8.0 writes `blueprints.json` once per session, so run the game once with the
  new plugin first (otherwise the chest is filled with plain chips).

  **Warehouse:** name a beacon **All** (or Everything, or Warehouse) and pick it to build every group at once: six rows of
  eleven platforms (the first row runs out from the beacon, each next row is beside it on the beacon's right hand), each
  group on platforms of its own, with bare foundations to finish any short row or top up the row to 11 deep, plus a blank
  aisle of bare foundations on the beacon's left and another after the last row (8 rows, 88 platforms, always laid flat).
  The rows are fixed in the code (butterfly larvae,
  larvae and petri dishes; frog eggs and tree seeds; plant seeds, vegetables and food; ores, rods and fuses; crafting
  materials, quartz, drones and essentials; equipment and tokens, fish eggs, toxic and purification). The first chests of
  each row carry index signs (`Butterfly <>`, `Rods >`, ...: `<>` is a group on both the left and right chests, `<` left
  only, `>` right only). The DNA and Genetics chests are holding tanks: titled, with no filter or demand, so Resupply leaves
  them alone. It is Container3 only, and needs about 88 platforms of clear, flat ground: only buildings are checked, not
  the terrain. **REMOVE WAREHOUSE** deletes exactly that 8x11 grid, its chests and everything stocked in them, and nothing else.
  **REMOVE WAREHOUSE** appears when one stands at the beacon: it deletes those platforms, the chests on them, their
  inventories and the items inside, and nothing else. It refuses if anything else (a machine, a wall) stands in the
  footprint, and, like every edit, it backs the save up first and checks the result before writing.

- **Factory**: autocrafters on a floor 10 m above the warehouse, always the full 8x11 floor laid, one crafter per platform
  for each product you tick (every autocrafter recipe the game knows, all ticked to start with), each set to make its
  product from the warehouse chests inside its 20 m range (a 3D sphere), with a sign saying what it makes (a crafter has
  no text label of its own). It plans from the game's own recipes (`recipes.json`, written by plugin 0.9.0 once per
  session: start the game with that plugin, load a world once). A product whose ingredients are not all in reach of some
  platform is listed as not built. Each crafter's inventory supplies its product to the drones, so the warehouse chest
  that demands it is filled, and a crafter stops by itself when its 8 output slots are full. Building also lays the two
  ways down to the warehouse the owner built by hand: the east ramp along the row nearest the beacon and the back ramp
  down the far (11th) column, reproduced tile for tile and turned to match the beacon's direction; a "Fix ramp" button
  re-lays either one on its own if it is ever disturbed. The plan
  warns how many MW the crafters draw once on (155 kW each). Also **REMOVE FACTORY** (the floor, crafters, signs and both
  ramps; the warehouse underneath is untouched), **ALL ON** / **ALL OFF**, a switch per crafter, and **Add crafter** /
  **Remove** for one crafter at a time, ad hoc, without rebuilding everything (removing keeps the platform's foundation).
  Everything follows the direction the warehouse beacon points. Only autocrafter recipes: nothing made in an incubator,
  the silk maker or another building.

## Which starts first, the game or the app?

Either. The dashboard reads the plugin's files, and with no fresh file it just says `WAITING FOR THE GAME`. It turns
`LIVE` as soon as the game is in a world, whether you opened the page before or after. If you close the game the
page keeps the last reading, dimmed, and says so.

## Principles

- **Read-only toward the running game.** The plugin observes; it never changes game state, saves, items or settings.
- **One deliberate exception, and it never touches the running game.** The Cheats page (Resupply, Drone Network, Base
  Building, Factory, Travel) edits a save file on disk, only while the game is at the main menu or closed. It backs the save up first, and refuses to write unless
  undoing its edit would give back the original file exactly.
- **Awareness, not shortcuts.** It shows what the game already knows; it does not help anyone bypass how the game is played.
- **One small contract.** Everything leaves the game through a few versioned JSON files (`live.json`, `live-world.json`,
  `blueprints.json`, `recipes.json`, `planets.json`, `pins.json`, and the `icons\` folder) ([docs/contract.md](docs/contract.md)).
- **Nothing of the game is redistributed.** The project references the game's assemblies in place. Game files, and the
  game's decompiled source, are never copied into this repo.
- **Nothing is trusted blindly.** Every section of the files is read on its own; if one fails it becomes `null` and the
  rest still arrive, and the dashboard copes with any of them missing.

## Setup

**See [INSTALL.md](INSTALL.md)** for the full steps on a new PC, including BepInEx. In short:

1. Install the .NET 10 SDK and the game (Steam), and back up your saves.
2. Install BepInEx 5.4.23.4 (win x64) into the game folder and run the game once.
3. `powershell -ExecutionPolicy Bypass -File tools\setup.ps1`: finds the game in any Steam library and writes
   `solution_private.targets` for the build.
4. Build the plugin (game closed): `dotnet build src/Live/Live.csproj`. It copies itself into
   `BepInEx\plugins\RRSOS-PCC-Live\`.
5. Run the dashboard: press start on the `Dashboard` profile in Visual Studio, or `dotnet run --project src/Dashboard`.
   It opens your default browser at http://localhost:5320 (full screen, F11, on a 2K monitor).

Settings for one PC go in `src\Dashboard\appsettings.Local.json` (git ignores it); every setting is listed in
`src\Dashboard\appsettings.json`.

To try the dashboard **without the game**, generate fake live files:
`tools\sample-live.ps1 -Path .\sample\live.json -Loop`, then
`dotnet run --project src/Dashboard --LiveFile=.\sample\live.json` (the world file is written beside it, and the base names are kept there too).
To see the bases, floor plans and extractors of a **real save**, `tools\save-to-world.ps1` turns one into the same two
files (it only reads the save).

## Where things are documented

| | |
|---|---|
| [INSTALL.md](INSTALL.md) | Installing on a new PC: BepInEx, setup script, building, settings, troubleshooting, uninstalling |
| [docs/handoff.md](docs/handoff.md) | Where things stand and what is next; start here in a new session |
| [docs/contract.md](docs/contract.md) | Every field of `live.json` and `live-world.json`, and how the floor plans, boneyard, antennas and vehicles work |
| [docs/game-notes.md](docs/game-notes.md) | What was found out about the game's code and data |
| [docs/test-plan.md](docs/test-plan.md) | What to check in the game, and what should happen |
| [docs/install-log.md](docs/install-log.md) | Exactly what BepInEx adds to the game folder, and how to undo it |

## What is built

| Area | State |
|---|---|
| Plugin skeleton, BepInEx install, game API discovery | done (Unity 6000.3, BepInEx 5) |
| Player, inventories, planet stats and rates, power and generators, rockets | done, run in the game and checked against it |
| Base, containers, extractors, drones (`live-world.json`) | done, run in the game |
| Floor plans (plugin 0.4.x): building pieces with measured shapes; launch, vehicle and trade platforms and the round compartment mapped by walking them in the game | done |
| Boneyard from the save (plugin 0.5.0): the live game's loose-item counts were unreliable, so it is read from the newest save | done |
| Antenna-synced radar sweeps (plugin 0.6.0) | done |
| Multiple vehicles (plugin 0.7.0) | done |
| Cheats / Resupply, Windows-voice alerts, setup script and install guide | done |
| Cheats / Drone Network and Base Building (18 groups, stocked equipment and blueprint chests) | done, run in the game |
| Blueprint list from the game (plugin 0.8.0, `blueprints.json`) | done |
| Cheats / Factory: autocrafters above the warehouse, planned from the game's own recipes (plugin 0.9.0, `recipes.json`), two ramps built automatically, local-chest and quarter-corner fallbacks for hard-to-reach products | done, run in the game |
| Home page Factory card: which products a base's autocrafters are short of | done |
| The departure platform found and drawn (plugin 0.10.1, hand-mapped from measurements in the game) | done |
| Multi-planet: power/rockets scoped per planet (plugin 0.10.2), a planet-name cache (plugin 0.10.3, `planets.json`), beacons/Drone Network/Resupply all planet-aware | done, run in the game |
| Rocket departure/arrival alerts (plugin 0.10.4) and terraformation phases, a Phases card and spoken milestones (plugin 0.10.5, `phases`) | done, run in the game |
| Spoken alerts: queued (not interrupted), a voice picker, volume and voice remembered | done |
| Drone map auto-opens on takeoff / closes on landing, with a pin | done |
| Pinned recipes (plugin 0.11.0, `pins.json`) kept in a shopping list with quantities and totals, coloured by what your backpack holds | done, run in the game |
| The game's own icons (plugin 0.12.0, `icons\`) beside items and buildings, on the power card, floor-plan containers and warehouse chests | done, run in the game |
| Drone Network: shows what a producer supplies now, **Fix** to the usual items, **Choose…**, `producer-supply-defaults.json`, and icons on its lines | done |
| Cheats / Travel: move the player to a spot on any planet in the save | done |
| Optional: local HTTP feed, in-game overlay | later |

## License

MIT: see [LICENSE](LICENSE). It covers this project's own code only. It does not include or redistribute anything from
The Planet Crafter or BepInEx, which you install yourself; the plugin only references the game's assemblies when it is built.
