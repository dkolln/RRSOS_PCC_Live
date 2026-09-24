# Handoff: where things stand and what comes next

Updated 2026-09-23, end of a long session. **Everything is committed and merged** (`master`, PRs #1 to #8 on
github.com/dkolln/RRSOS_PCC_Live); nothing is waiting in the working tree. The plugin in the game folder is **0.7.0**.
This top section is current; the older write-ups further down are history.

## Where things are

| | |
|---|---|
| Repo | `C:\Users\david\source\repos\Planet Crafter\RRSOS-PCC-Live` (branch `master`) |
| Game | Steam, `C:\Program Files (x86)\Steam\steamapps\common\The Planet Crafter\` (BepInEx 5.4.23.4); `tools\setup.ps1` finds it on any PC |
| Plugin output | `%LOCALAPPDATA%\RRSOS-PCC-Live\live.json` (1 s) and `live-world.json` (5 s); also the dashboard's own files (base names, notes, resupply configs, save backups) |
| Saves | `%USERPROFILE%\AppData\LocalLow\MijuGames\Planet Crafter` — `Custom-1.json` is the one being played, `Custom-2.json` older/bigger |
| Decompiled game source | still present (checked 2026-09-23) at `C:\Users\dkoll\AppData\Local\Temp\claude\C--Users-david-source-repos-Planet-Crafter-RRSOS-PCC\bd791197-e25f-4eb1-8f89-dda0e4b8fb25\scratchpad\decomp\SpaceCraft` (686 files). Regenerate with the `ilspycmd` line at the bottom if it is gone |
| Install on a new PC | `INSTALL.md` (BepInEx, `tools\setup.ps1`, build, settings, troubleshooting) |
| Data formats | `docs/contract.md` — the authority on every field, and on the floor plans, the boneyard, the antennas and the vehicles |

## What was built this session (all in the game, confirmed by the owner)

- **Floor plans** (Base card, under the inventory; `Classes/FloorPlan.cs`, `Instruments/BaseFloorPlan.razor`). The plugin
  reports every building piece (`structures`: pods of all shapes, foundations, platforms, domes, labs, T2 aquarium,
  ladders) with collider boxes measured in each piece's own frame, plus each wall panel's box. The dashboard draws one
  floor at a time (▲/▼, follows the player's floor; the label goes green while following). Floors are found from height
  gaps over 2.5 m. Details and all the numbers: `docs/contract.md`, "Building pieces".
  - Save panel order for a plain pod is +Z, −Z, +X, −X (confirmed live).
  - **Hand-mapped shapes** (the measured boxes lie for these; the owner walked their corners in the game):
    launch platform (6 m tiles, 3 rows of 8 + a row of 7, north stairs, a landing 10.6 m up, a two-tile tower whose
    top 30 m up is a floor; floors 4 and 5 show the tower only), and vehicle platform (measured deck with both −X
    corners cut diagonally, ramp off +Z across the two +X tiles, console marker). Platforms' decks are 5 m above
    their position.
  - **Not mapped yet: the trade platform** — the owner has not built one. When they do: same corner walk, then a
    fixed shape like the other two in `FloorPlan.cs`.
- **Boneyard from the save** (plugin 0.5.0 dropped live loose items; the counts were wrong). `SaveLooseService` reads
  the newest save (not `Backup.json`) every 10 s when it changes: records with a `"pos"` are in the world. Same
  raw-material + nearest base within 100 m rules; this planet only (`planetHash`). Title shows the save's time.
- **Spoken alerts through Windows** (`SpeechService`, `System.Speech`): the default voice from Windows Settings >
  Speech; cooldown shared across tabs. `alerts.js` is gone.
- **Cheats: Replace all / fill-only** per config, Select all / none; old configs read as Replace all. Algae
  (`Algae1Seed`) is named "Algae" now, as the game names it.
- **Antenna-synced radar sweeps** (plugin 0.6.0, `AntennaReader`; dashboard `wwwroot/js/radar.js`). The Transmission
  Antenna's dish is `Radar_Base_01`, spun by the game's `Turn_Move` at 50°/s clockwise; the dish faces −90° from that
  part's forward axis (calibrated by the owner with the "Antenna faces N" button under the mini maps, then confirmed
  by eye). All the mini-map sweeps follow the nearest antenna; without one they spin on their own at the dish's
  speed (one turn every 7.2 s). Out of the game's render range the dish stops (the plugin reports rate 0); the sweep
  then carries on at the last speed seen, re-syncs at once when the dish turns again, and otherwise only every 5th
  reading (`SyncRadar` in `Home.razor`).
- **Multiple vehicles** (plugin 0.7.0: `vehicles` in `live.json`, every `VehicleTruck`, oldest first; `vehicle` kept as
  the first). `VehicleMap` / `VehicleList` / `VehicleDetail` work like the extractors: click the map for the list, a
  pip or a truck for its detail, the map again to close. Trucks have no names in the game: "Truck 1, 2, ..." in the
  order they were built. The owner has two in Custom-1: Truck 1 (id 204017955, on the vehicle platform) and Truck 2
  (id 204906349).
- **Hand edits to Custom-1, asked for by the owner** (outside Cheats; game closed; backed up to `save-backups\` first;
  checked that undoing the edit gives back the original): copied Truck 2's three modules (Equipment increase T1,
  Inventory increase T1, Lights T1) into Truck 1, and set Truck 1's trunk to 48 slots. Learned: the game raises a
  truck's **module slots** itself on load when an Equipment increase module is in it (2 → 5), but does **not** raise
  the **trunk** for an Inventory increase module; that one needed the save edited (30 → 48, matching Truck 2).
- **Portability**: `tools\setup.ps1` (finds the game in any Steam library, writes `solution_private.targets`), build
  falls back to Steam's registry path and gives a clear error, every dashboard setting listed in `appsettings.json`,
  per-PC overrides in git-ignored `appsettings.Local.json`, `INSTALL.md`.

## Decided, so don't redo

- Base card lists are **not** filtered by floor: all items in a base, whatever floor, is what the owner wants.
- The boneyard is **save-based on purpose**. Don't bring back live loose-item reading.
- The original test plan's open items (Energy Levels and Terraformation comparisons, the compass in 8 directions,
  stowed truck, the long main-menu wait) were **confirmed by the owner** and ticked in `test-plan.md`. The rest of
  that checklist was never ticked item by item, but everything it covers has been in daily use.

## Next

Nothing is in progress. Open ideas (none asked for yet): the trade platform once built; more spoken alerts (an
extractor or grower full, power deficit); loose items or flying drones drawn on the floor plan.

## How the owner works (in addition to the rules further down)

- "commit, push and merge" means: a branch, one commit, a PR on github.com/dkolln/RRSOS_PCC_Live, merge with a merge
  commit, delete the branch, back to an up-to-date `master`. Only when asked.
- The owner often runs the dashboard themselves on **port 5320** (debug session). To check something without getting
  in the way, run a copy on **port 5321** (`--no-launch-profile -- --urls=http://localhost:5321 --environment=Development`;
  add `--LiveFile=...`, `--LiveFolder=...` or `--SaveSettings:SavePath=...` for test data), and **stop it when done**:
  a leftover server on 5320 once kicked the owner out with a "cancelled" error. While their dashboard runs, build
  into a scratch folder (`dotnet build src/Dashboard -o <scratch>`) because their process locks `bin\`.
- The plugin can only be deployed with the game **closed** (check `tasklist` for `Planet Crafter`); compile-check with
  `-p:PluginsFolder=<scratch>\` otherwise. New plugin code needs a full game restart.
- Shapes and directions in the game are best settled with the owner **standing on the spot**: read their position from
  `live.json` (`player.position`, `yawDegrees`) and compare. Chat messages arrive seconds late, so anything timed (like
  the spinning dish) needs a button on the page instead.
- Test anything that edits saves on a **copy** (scratch save folder + scratch `LiveFolder`), never the real files.
- One-off save edits the owner asks for ("copy modules into the other truck"): only with the game closed (it would
  overwrite them), dry run first, back up to `%LOCALAPPDATA%\RRSOS-PCC-Live\save-backups\`, edit only the records
  involved (new records get fresh ids in 200,000,000 to 210,000,000), keep the file's BOM, and check that undoing the
  edit gives back the original byte for byte. Explain any catch (like too few slots) and let the owner choose.


## History: what worked as of 2026-09-21 (details superseded above win)

**Verified in the game (2026-09-21, plugin 0.2.0):** player position, yaw and vitals (with maximums), backpack, gear, six
planet stats with live per-second rates, power (produced, used, generators by kind), rocket counts and multipliers, and
the vehicle (position, trunk, gear). Rocket multipliers matched the wiki maths exactly, the vehicle position matched a
save, and the owner reviewed the dashboard live and was delighted. Later the same day the owner confirmed again that
player, vehicle, power and planet data "all look good".

**Base, Extractors and Drones (plugin 0.3.0):** now fully run and iterated on in the game across several sessions
(see above for what changed most recently). Original build notes:

- **Plugin:** `live-world.json`, written about every 5 seconds by `WorldPoller` (a coroutine), using `WorldScan`, which
  walks the game's placed objects in three passes and spreads the work over frames (about 2 ms each). It reports **raw
  facts**: pods with their panels, signs, containers (with their items) within 120 m of a pod, loose items near a pod, and
  ore, gas, water and algae extractors (product, size, count, contents). It also reports its own cost (`scan`). See `contract.md`, "The world file".
- **Dashboard:** `WorldFileService` reads that file and works out the bases with `BaseDirectory` (a base is a pod with a door;
  door plus connection is a "Base", else an "Outpost"; things belong to the nearest base within 100 m; names from signs, then
  a saved name, then a pool: `BaseNames`).
- **Ported from RRSOS-PCC (copied, not referenced):** `BaseMap`, `BaseContents` (built on the existing `ItemList`), `BaseSelector`,
  the extractor grouping, the item-type table (`Assets/worldobjectdata.json` and `ItemType`/`ItemCatalog`) and the two name pools.

**Drones:** flying drones are read every second into `live.json` (`DroneReader`: `FindObjectsByType<Drone>` re-listed every 5 s, then positions, task and cargo);
drone stations are in the world file (`droneStations`). See `contract.md` ("Drones") and `test-plan.md` section 10.

Not yet confirmed by the owner from the original list: the Energy Levels and Terraformation screen comparisons, the
compass in all eight directions, whether a stowed truck's trunk and gear still show, and the long main-menu wait.
See `test-plan.md` (tick items off as they are done).

## Working rules that emerged (from the owner)

- Read-only toward the running game, awareness not shortcuts. Never change live game state — the plugin only
  observes. Cheats/Resupply is a deliberate, separate exception scoped to editing the save file on disk while the
  game is not in a world; it does not touch the running game.
- RRSOS-PCC stays save-file based; do not make the two depend on each other.
- Ask before committing or pushing. Commits only when explicitly asked, even after a long stretch of changes.
- Changing the game folder (installing or updating BepInEx or the plugin) needs the game closed for the build to
  copy the DLL in, then the game needs a **full restart** (not just a world reload) to actually load new plugin
  code — BepInEx does not hot-swap a running plugin from a file overwrite.
- Test the dashboard against fake data (`tools\sample-live.ps1`) or a real save (`tools\save-to-world.ps1`) first;
  for anything touching the plugin's read of live game state, verify with real data before trusting it (this
  session's Boneyard bug is the case study for why — id-range assumptions and even a live in-game test can both
  mislead if you don't cross-check against the actual game source).
- The owner's saves: `Custom-1.json` (currently the active one) and `Custom-2.json` (older, larger, flagged
  `modded`). Never edit them directly outside of Cheats/Resupply, which the owner explicitly drives.

## Facts worth remembering

- Compass: north is world +X, east is world -Z (measured in the game). `Compass.ToEastNorth` in the dashboard, `PCMath.ToEastNorth` in RRSOS-PCC.
- The game marks saves `modded: true` while `BepInEx\plugins` is non-empty. Cosmetic.
- Fandom wiki pages cannot be fetched by tools (HTTP 402) but open fine in a browser; the wiki's tables match the game.
- A vehicle is a world object with group id `VehicleTruck`; a stowed vehicle has no position. Since plugin 0.7.0
  every truck is reported (`vehicles`), and the dashboard shows them all (see "Multiple vehicles" above).
- **Object ids** (corrected/expanded this session): `WorldObjectsIdHandler.IsWorldObjectFromScene(id)` is
  literally `id < 200,000,000`, and `WorldObjectsIdHandler.GetNewWorldObjectIdForDb()` proves anything created
  during play always gets `id >= 201,000,000`. This is purely an "object age" signal (existed since world load,
  or created later) — **it is not a reliable signal for "is this a real, loose, pickupable item"** versus
  landscape decoration. World generation can place genuinely-loose ore chunks directly, and they keep a
  "from scene" id forever. Panel value 4 is a door, 2 a corridor connection (unrelated, still true).
- A shell quirk on this machine: a long `bash` heredoc with several files in one command can fail to parse; write files one at a time.
- To look at the dashboard while developing: the launch config in `.claude/launch.json` (ignored by git) starts it on fake or converted data; regenerate the decompile
  with `ilspycmd -p -o <a scratch folder> "<game>\Planet Crafter_Data\Managed\Assembly-CSharp.dll"` (about 30 seconds, kept out of the repo).
