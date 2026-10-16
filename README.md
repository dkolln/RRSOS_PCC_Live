# RRSOS PCC Live

A live view of **The Planet Crafter**: a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that reports the running
game's state to two small JSON files, and a local dashboard that shows it on one page in your browser. The plugin only
ever observes the game.

It is a separate project from [RRSOS-PCC](https://github.com/dkolln/RRSOS-PCC), the companion dashboard. That app works
only from the game's **save file**, which the game writes only when it saves, so nothing in it can be truly live. This
project reads the running game instead and never reads a save file. Neither project depends on the other to build.

```
Planet Crafter  --(plugin, every second)----> live.json        --(watches)-->  Dashboard (browser)
                --(plugin, every 5 seconds)-> live-world.json  --(watches)-->
                --(plugin, when you pin)-----> pins.json        --(watches)-->
                --(plugin, once per session)-> recipes.json, blueprints.json, icons\*.png
                                              %LOCALAPPDATA%\RRSOS-PCC-Live
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
  count and multiplier under each, the total TI, a **Phases** row and the **Power / Toxicity** card. Power is the power dial with one icon per generator. Toxicity (on a
  planet that has purification or toxic goo) has the **Purification** dial, balanced against the other stats like they are and counted in
  the total TI, and two **progress bars**, red under a fifth, yellow under half, green from half up (the gauges' own zones): goo collected (with the goo icon) and toxic areas cleaned (plugin 0.14.0). The tab you pick is remembered. The other tab's headline
  (surplus or deficit, or the purification total) sits at the right of the card header. Under it, a notes card.
- The **Factory** tab (third tab of that card, shown once the planet has autocrafters) summarises the factory you are at: the cluster of autocrafters nearest to you (each within 15 m of another).
  It counts the ingredients with **no supply** (no crafter in the factory makes them, no extractor or collector on the planet is set to them *and* told to supply them to the drones (plugin 0.17.0 reports that; a harvesting robot set to an item counts like an extractor), and they are not delivered by the rocket depot, grown (crops, silk, honey) or collected as algae), the ones that are
  **short** (a crafter finds less than one craft's worth in the containers within its own 20 m reach), and the crafters that are **empty**, **full** (all 8 output slots taken) or working. A zero-sum
  factory has no ingredient without a supply. Click the summary for the details window: each missing ingredient with its supply, how many crafters are short of it and where the stock is (for example
  "90 in storage, but not within reach": it needs a demand chest near the crafters), then the empty crafters (and what each is waiting on), the full ones and the working ones. One crafter's product counts as
  supply for another. The rocket depot's Selenium, Phosphorus, Tungsten and Amber, and grown things (crops, algae, honey, silk) are assumed supplied (`FactoryReport`). Needs the recipe list (plugin 0.9.0, `recipes.json`).
- **Click a gauge** (oxygen, heat, pressure, plants, insects, animals, purification) to see every building the game has that makes that
  stat, strongest first: how many you have built, what one makes, and what they make together right now (each machine's own
  figure, boosts included). Buildings you have not unlocked yet are shown in grey with a lock, and a tick hides them;
  you can sort by what they make now or by what one makes. (Plugin 0.13.0, `terraformers.json`.)
- The **power card** lists one row per kind of generator (wind, solar T1/T2, nuclear T1/T2, fusion; only those you have): its icon, how many
  are built and what they make together. Optimizers make single machines differ, so there is no per-machine figure.
- **Click the power gauge** to see what makes the power and what uses it: producing (weakest generator to strongest) and using
  (hungriest machine to least), each with how many are built, what one makes or draws, what they do together right now and
  their share. A button on each list flips its order. The using list needs plugin 0.16.0.
- **Phases**: the current planet's own named terraformation milestones (the game's "progress" screen â€” Lakes, Animals,
  Complete Transformation, ...; not every one lines up with a gauge), each planet with its own list: solid once
  complete, flashing for the one in progress, plain for the rest, with a live percentage toward the next one.

**Shopping list (right, under the detail card)**: every recipe you pin in the game (the Blueprint pinning microchip, top
right of the screen) lands here and **stays after the pin is cleared**, so the game's few pin slots are no limit. Each
entry has a quantity (âˆ’ / +) and a âœ•; below them, the **totals** of every ingredient across the whole list. A total turns
**yellow** when your backpack holds some of it and **green** when it holds all of it. An **ids** switch shows game ids
instead of names where space is tight. Pinning something already on the list does not add it twice. **+ item** opens a search box. Each match has two buttons: **item** adds just that item (the fuses to go in an enhancer, say) as its own line and total, and **breakdown** adds it the way a pinned recipe is added, the item on top and what it takes to make below (greyed when the game has no recipe for it). Picking one already there wants one more. The list is kept in
`shopping-list.json`; the dashboard has to be running while you pin.

**Icons**: the game's own icon for every item and building sits beside its name in the backpack, gear, trunk, base and
extractor lists, the shopping list, the truck tiles, and on the power card (the machine itself, in place of the little
drawn windmill, atom and so on; the drawing is still used when an icon is missing). The floor plan stays plain (too many icons together), and hovering a container there
names its product. The
plugin writes them out of the running game once (`icons\<group id>.png`, plugin 0.12.0), so nothing is bundled or copied
from anywhere else.

**Detail card (right)**: shows whichever map you last clicked.
- **Base**: what the base you are at (or picked on the map) holds: stored items (a **Hide full single-item containers**
  tick leaves out every Container3 holding 80 of one item, which is what a stocked warehouse chest is, so the rest of what
  you store is not buried; it says how many it hides and is remembered between runs), crops ready to harvest, the
  the **floor plan**, and, once
  that base has a factory, a **Factory card**: which products its autocrafters are short of ingredients for, click a
  line to see which crafters.
- **Floor plan**: the base drawn from above, one floor at a time (â–²/â–¼, or it follows the floor you stand on): pods, walls,
  windows and doors, foundations, platforms (launch, vehicle, trade, **the departure platform**), domes, labs, ladders,
  containers and you.
- **Drones**: flying drones with their tasks and cargo, and every drone station with what is docked in it. The map opens
  itself the moment a drone takes off and closes back to Base once every one has landed; a ðŸ“Œ pin stops that when you
  want to stay put (picking the drone view by hand pins it, picking anything else un-pins it).
- **Vehicles**: every truck (Truck 1, 2, ...), where it is or that it is stowed, its trunk and modules.
- **Extractors**: every ore, gas, water, toxic water and algae machine, grouped, with fill level, position, and distance and
  direction from you.

**Also**
- **Spoken alerts**, through a Windows voice (a picker next to the volume slider lists every one installed; your pick
  and the volume are both remembered): low oxygen, health and thirst, and toxicity ("Toxicity is low" at half your tolerance left, "critical" at a fifth); a trade or interplanetary-exchange rocket
  departing or arriving; a terraformation phase completing or the next one starting, and once per planet,
  "Terraformation is complete". Two landing close together are both heard in full (they queue, with a short pause,
  rather than a newer one cutting an older one off).
- **Multiple planets**: power and rockets show which planet is which (a `planets.json`
  cache learns each one's own name as you visit it) and scope to the one picked, so a beacon or a container label
  shared by two planets is never pooled or mixed up with the other's.
- **LAUNCH PC** starts the game through Steam. It is disabled while the game runs.

Lists (backpack, gear, trunks, stored items) can be folded by clicking their titles.


## Which starts first, the game or the app?

Either. The dashboard reads the plugin's files, and with no fresh file it just says `WAITING FOR THE GAME`. It turns
`LIVE` as soon as the game is in a world, whether you opened the page before or after. If you close the game the
page keeps the last reading, dimmed, and says so.

## Principles

- **Read-only toward the running game.** The plugin observes; it never changes game state, saves, items or settings.
  This holds for every release up to and including **v0.17.1**, the last read-only version. It is kept alive on the
  `read-only-maintenance` branch, which only gets fixes for game updates (see "The read-only cutoff" below).
- **Awareness, not shortcuts.** It shows what the game already knows; it does not help anyone bypass how the game is played.
- **One small contract.** Everything leaves the game through a few versioned JSON files (`live.json`, `live-world.json`,
  `blueprints.json`, `recipes.json`, `planets.json`, `pins.json`, `terraformers.json`, and the `icons\` folder) ([docs/contract.md](docs/contract.md)).
- **Nothing of the game is redistributed.** The project references the game's assemblies in place. Game files, and the
  game's decompiled source, are never copied into this repo.
- **Nothing is trusted blindly.** Every section of the files is read on its own; if one fails it becomes `null` and the
  rest still arrive, and the dashboard copes with any of them missing.

## The read-only cutoff

From the version after v0.17.1 the live-write side grows: mods that place things in the running game, for example building
the main base piece by piece as the matching things unlock. **Those mods live in their own repository,
[RRSOS-PCC-Mods](https://github.com/dkolln/RRSOS-PCC-Mods)**, completely separate from this one. This repository (the dashboard
and the read-only plugin) keeps the read-only rule; this section records the decision so the line is clear.

- **Last read-only version:** tag `v0.17.1` (plugin 0.17.1). The `read-only-maintenance` branch starts at that tag, is
  protected like `master`, and only receives fixes needed for game updates.
- **`master` here** stays the dashboard and the read-only plugin.
- **Live writes are separate plugins** in RRSOS-PCC-Mods, each with its own name and BepInEx plugin id, so a read-only
  install and a live-write install can never be mixed up, and the extra game-update upkeep applies to those plugins only.
- **Live writes are opt-in:** off by default, every placement logged, and a way to undo what a mod placed.

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
| [docs/contract.md](docs/contract.md) | Every field of `live.json` and `live-world.json`, and how the floor plans, antennas and vehicles work |
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
| Antenna-synced radar sweeps (plugin 0.6.0) | done |
| Multiple vehicles (plugin 0.7.0) | done |
| Blueprint list from the game (plugin 0.8.0, `blueprints.json`) | done |
| Home page Factory card: which products a base's autocrafters are short of | done |
| The departure platform found and drawn (plugin 0.10.1, hand-mapped from measurements in the game) | done |
| Multi-planet: power/rockets scoped per planet (plugin 0.10.2), a planet-name cache (plugin 0.10.3, `planets.json`), planet-aware power and rockets | done, run in the game |
| Rocket departure/arrival alerts (plugin 0.10.4) and terraformation phases, a Phases card and spoken milestones (plugin 0.10.5, `phases`) | done, run in the game |
| Spoken alerts: queued (not interrupted), a voice picker, volume and voice remembered | done |
| Drone map auto-opens on takeoff / closes on landing, with a pin | done |
| Pinned recipes (plugin 0.11.0, `pins.json`) kept in a shopping list with quantities and totals, coloured by what your backpack holds | done, run in the game |
| The game's own icons (plugin 0.12.0, `icons\`) beside items and buildings, on the power card, floor-plan containers and warehouse chests | done, run in the game |
| Click a planet gauge to see what makes that stat: every building, strongest first, built count, live output, locked ones in grey (plugin 0.13.0, `terraformers.json`) | built, dashboard checked on sample data; the plugin side not yet run in the game |
| Optional: local HTTP feed, in-game overlay | later |

## License

MIT: see [LICENSE](LICENSE). It covers this project's own code only. It does not include or redistribute anything from
The Planet Crafter or BepInEx, which you install yourself; the plugin only references the game's assemblies when it is built.
