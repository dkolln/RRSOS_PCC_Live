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
  count and multiplier under each, the total TI, the power dial and one icon per generator. Under it, a notes card.

**Detail card (right)**: shows whichever map you last clicked.
- **Base**: what the base you are at (or picked on the map) holds: stored items, crops ready to harvest, and the
  **boneyard** (loose ore, alloy, quartz and rods lying around, read from the last save), plus the **floor plan**.
- **Floor plan**: the base drawn from above, one floor at a time (▲/▼, or it follows the floor you stand on): pods, walls,
  windows and doors, foundations, platforms (launch, vehicle), domes, labs, ladders, containers and you.
- **Drones**: flying drones with their tasks and cargo, and every drone station with what is docked in it.
- **Vehicles**: every truck (Truck 1, 2, ...), where it is or that it is stowed, its trunk and modules.
- **Extractors**: every ore, gas, water and algae machine, grouped, with fill level, position, and distance and
  direction from you.

**Also**
- **Spoken alerts** for low oxygen, health and thirst, in your Windows default voice (Settings > Time & language >
  Speech), with a volume slider.
- **LAUNCH PC** starts the game through Steam. It is disabled while the game runs.
- **CHEATS** (only while the game is not in a world): three tabs that edit a save file on disk, see the principles
  below and the next section.

Lists (backpack, gear, trunks, stored items) can be folded by clicking their titles.

## Cheats: editing a save

Every Cheats tab works on a save file, only while the game is at the main menu or closed. Each edit makes a byte-exact
backup first, writes atomically, and is refused unless undoing it would give back the original file exactly.

- **Resupply**: a configurable list of items to top up in your inventories.
- **Drone Network**: sets which containers supply and which demand, so drones move things without hand setup. A
  container is either a demand sink or a supply source; producers supply what they hold, extractors and ecosystems
  supply everything. Pick which producers and consumers to switch on, and choose the item a container demands.
- **Base Building**: builds a whole row of storage.
  1. Place a foundation with a **beacon** whose text names the job (for example "Fish"); the way the beacon faces is
     the way the row grows.
  2. Build one platform of chests (Container1, 2 or 3) as a sample and **capture** it as a template.
  3. Pick the beacon, a **group** and a template. The tab shows the plan (an SVG preview, the labels, anything in the
     way) and, on **BUILD ROW**, writes the platforms and chests into the save.

  Each chest is labelled and filtered with the next item of the group, nearest platform first and left chest before
  right. Options: set every chest to demand its item, and fill it. There are 18 groups: fish eggs, frog eggs,
  butterfly larvae, tree seeds, plant seeds, vegetables, larvae, petri dishes, quartz crystals, rods, ores, crafting
  materials, fuses, food and cooking, toxic and purification, drones, essentials, and **Equipment and tokens**.
  That last one stocks one chest each with one of every spacesuit, personal item and vehicle item, a **Blueprints**
  chest with one linked chip for every building you have not unlocked yet (split over several chests when the
  template is small), and a box of terra tokens. It needs a Container2 or Container3 template. The blueprint list
  comes from the game itself: plugin 0.8.0 writes `blueprints.json` once per session, so run the game once with the
  new plugin first (otherwise the chest is filled with plain chips).

## Which starts first, the game or the app?

Either. The dashboard reads the plugin's files, and with no fresh file it just says `WAITING FOR THE GAME`. It turns
`LIVE` as soon as the game is in a world, whether you opened the page before or after. If you close the game the
page keeps the last reading, dimmed, and says so.

## Principles

- **Read-only toward the running game.** The plugin observes; it never changes game state, saves, items or settings.
- **One deliberate exception, and it never touches the running game.** The Cheats page (Resupply, Drone Network, Base
  Building) edits a save file on disk, only while the game is at the main menu or closed. It backs the save up first, and refuses to write unless
  undoing its edit would give back the original file exactly.
- **Awareness, not shortcuts.** It shows what the game already knows; it does not help anyone bypass how the game is played.
- **One small contract.** Everything leaves the game through a few versioned JSON files (`live.json`, `live-world.json`, `blueprints.json`) ([docs/contract.md](docs/contract.md)).
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
| Optional: local HTTP feed, in-game overlay | later |

## License

MIT: see [LICENSE](LICENSE). It covers this project's own code only. It does not include or redistribute anything from
The Planet Crafter or BepInEx, which you install yourself; the plugin only references the game's assemblies when it is built.
