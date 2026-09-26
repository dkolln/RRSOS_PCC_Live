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
  - **Round 3x3 living compartment** (`Pod9x*`, seen as `Pod9xA`; 2026-09-26): drawn as a **circle**, 24 m across, centred on
    its position, not the old 12 m square guess. From what the plugin measured while the owner stood in it: box -12 to +12 on
    both axes, a flat floor slab ~23.2 m wide, the four wall panels on a ring 11.4 m out, and a 3x3 grid of 6 m tiles (the
    corner ones the rounded ones) inside.
  - **Domes and the aquarium are centred on the middle of their box, not on their position** (found 2026-09-26): a T2
    dome's box runs -20 to +12 on its x, so its middle is 4 m from its position. The owner stood in the middle of the
    Butterfly dome and read 3.95 m along (the box middle to 0.1 m); in Biodome2 (identical box) 2.6 m from the predicted
    middle (6.6 m from the position). The circle's radius is the box's shorter half (13.6 for the T2 dome, 10.1 for the small
    `biodome`), and what the longer half reaches past it is an entrance annex on **both** ends (2.4 m; 1.9 m for the small
    one). That is why the rows of domes now join: centres 32 m apart, 13.6 + 13.6 leaves 5 m, two 2.4 m annexes meet. Drawing
    the circle on the position had put it 4 m off and made one annex look 8 m long (the owner: "grossly exaggerated"). An
    earlier 3 m cap on annexes was a wrong fix for that and is gone.
  - **Trade platform** (mapped 2026-09-25, from the owner standing on it while the game ran; my reads of `live.json` are
    seconds late, so they stand still and say "here"): a deck of 3 foundations (6.09 m each) east-west by 2 north-south,
    18.27 x 12.18 m, no cut corners, plus 3 m wide stairs (half a foundation) off the middle of the east end, 6.88 m long,
    down to the ground. In its own frame (yaw 180: x runs south, z runs east): deck x -6.09 to 6.09, z -10.1 to 8.17;
    stairs x -1.5 to 1.5, z 8.17 to 15.05. The plugin's measured box (z -10.1 to 15.05) is exactly deck plus stairs, and
    the owner's corner readings were the middles of foundations (north/south ±3.1 to 3.4 m, 5.2 m east, -7.8 m west).
    A **trade rocket circle** (`Trade rocket`, `PlanPart.Rocket`, dashed red) lands across the middle of four foundations: radius
    3.045 m (half a foundation; a guess from "about a foundation", which the owner may want as 6.09) centred on the corner they
    share, deck-frame (0, -4.01). Its **entrance** is a yellow arc (`Trade rocket entrance`, `PlanPart.RocketEntrance`) on the
    circle's **east** edge (angle 90°; the first guess, west, was exactly backwards, per the owner), width 40° (a guess, ~2 m); `TradeRocketEntranceAngle` / `TradeRocketEntranceWidth` in `FloorPlan.cs`. The owner stood 2.8 m from that centre; the plugin's deck box also found a flat 6 m slab there.
    A small square (1.2 m, `Trade console`, the desk with the console) marks where the owner stood facing it: deck-frame x
    2.9 to 4.1, z 2.15 to 3.35, placed 1 m ahead of that spot, so its distance is a guess. Not checked: the bottom of the
    stairs, and the stairs' exact width (the owner said "roughly half a foundation").
- **Boneyard from the save** (plugin 0.5.0 dropped live loose items; the counts were wrong). `SaveLooseService` reads
  the newest save (not `Backup.json`) every 10 s when it changes: records with a `"pos"` are in the world. Same
  raw-material + nearest base within 100 m rules; this planet only (`planetHash`). Title shows the save's time.
- **Spoken alerts through Windows** (`SpeechService`, `System.Speech`): the default voice from Windows Settings >
  Speech; cooldown shared across tabs. `alerts.js` is gone.
- **Cheats: Replace all / fill-only** per config, Select all / none; old configs read as Replace all. Algae
  (`Algae1Seed`) is named "Algae" now, as the game names it. Resupply toasts: **only for a container that actually
  had something added** (the owner asked; a failed run still shows its error, since a save that was not changed must
  never look changed). Item table: seeds `Seed1` to `Seed4` are Shanga, Pestera, Nulna, Tuska; the tree seeds
  `Tree0Seed` to `Tree13Seed` are "Tree Seed" + Iterra, Linifolia, Aleatus, Cernea, Elegea, Humelora, Aemora, Pleom,
  Soleus, Shreox, Rosea, Lillia, Prunea, Ruberu (the owner's list, matching RRSOS-PCC's `worldobjects.json`). The
  **Item-id containers** (Resupply, after the configured rows; `SaveResupplyEngine.Apply`'s `autoItem`): a container
  whose label is an item's own game id in any letter case ("MalisseaH", "tree5seed", "Iron") is filled with that item;
  only empty slots by default, or replace-all with the option ticked (both options are on the Cheats page and saved in
  `resupply-configs.json` as `AutoFillByGId`, on by default, and `AutoFillReplaceAll`, off). Skips containers a config
  already handled, and labels that are buildings (Machine, BasePart, Container, WorldMarker, Wreck); an id only counts
  if it is in `worldobjectdata.json`. The owner's Custom-1 has 13 chests labelled Tree1 to Tree13 (15 slots each); their Resupply configs for them are in
  the owner's `resupply-configs.json` (per-PC data, not in the repo). Both repos carry the same name table.
  Fish eggs: `Fish1Eggs` to `Fish11Eggs` are "Fish Eggs" + Provios, Vilnus, Gerrero, Khrom, Ulani, Aelera, Tegede, Ecaru, Buyu, Tiloo,
  Golden; **`Fish12Eggs` is Velkia** (confirmed in the owner's Custom-1 backpack, 2026-09-25), and `Fish13Eggs` Galbea and
  `Fish14Eggs` Stabu are the owner's best guess (the list they gave named `FishGalbea`, `FishVelkia`, `FishStabu`, which the
  game does not use; there are no such records in a save). The list also gives biomass multipliers, not stored.
  Frog eggs: `Frog1Eggs` to `Frog16Eggs` are "Frog Eggs" + Generic, Huli, Felicianna, Strabo, Trajuu, Aiolus, Afae, Cillus, Amedo,
  Kenjoss, Lavaum, Leglus, Jumi, Seren (Selenea Expansion), Acuzzi, Toxifia; `FrogGoldEggs` is "Frog Eggs Golden" (the owner's list).
  Spacesuits: `Skin-01`..`Skin-07`, `09`, `13`..`20` are "Spacesuit" + Comto, Blasto, Primo, Goldeo, Scifo, Cipto, Beteo,
  Fablo, Mekio, Abyso, Ettio, Plesao, Rorao, Starforma, Glitx, Colonnya (DLC tags kept in the name; the owner's list).
  `Skin-08/10/11/12` were not on it and keep RRSOS-PCC's names (Tureo, Tubio, Vateo, Widio).
  Butterfly larvae: `Butterfly1Larvae` to `Butterfly20Larvae` are "Butterfly Larva" + Abstreus, Alben, Azurae, Chevrone,
  Empalio, Fensea, Fiorente, Futura, Galaxe, Leani, Liux, Lorpen, Nere, Oesbe, Penga, Aemel, Golden, Imeo, Faleria,
  Feliciana (the owner's list; RRSOS-PCC's old names for these ids were wrong).
- **Cheats has a sub-menu** (tabs under the header: Resupply, Drone Network). **Drone Network** (`DroneNetworkEngine`,
  `SaveResupplyService.DroneNetworkAsync`, same backup / atomic write / undo-check as Resupply): needs at least one
  `DroneStation*` in the chosen save; shows a read-only preview, then **Set Supply Lines** writes. The game keeps a
  container's or machine's drone settings on its *inventory* record (`demandGrps`, `supplyGrps` = comma lists of item
  gIds, `priority`; never-configured records lack all three). Every configured record carries its own full copy of a
  list, so a "supply everything" list is ~2.8 KB per record: the owner ruled that out except for one chest. The owner's
  model, which is all the tool writes: drones carry an item from something that supplies it to something that demands
  it. A **producer** supplies what it makes (the kinds of item it holds; pick an item if empty; **ore and gas extractors and ecosystems supply everything**, since they pull random ore, gas or larvae; autocrafters are not
  producers here, they hold ingredients too). A **container** (`Container1/2/3`) is a **sink**: it demands its one
  product and supplies nothing (a demand chest also feeds any autocrafter in range, no drone involved), never both.
  Its product is what its label names (item id, or a unique item name like "Mushroom", `ItemCatalog.ResolveLabel`), else
  the one thing it holds when all alike; mixed ones ("Misc", unlabelled with many items) are ignored unless an item is
  picked. The one exception is the **Supplier** checkbox: a chest that supplies every item (the owner's pocket-emptying
  chest at the base entrance); "everything" is learned from the save (longest supply list + what others add; 211 in
  Custom-1, 206 in Custom-2), with a built-in Custom-1 list (`DroneNetworkEverything.cs`) as a fallback. **Nothing is
  changed unless picked**; old-style chests (demand X + supply all but X, which the game's own UI produced) show as
  "Demands it and supplies more" with a Fix button. Tested on copies of both saves (only the picked inventories changed,
  items and slots untouched).
- **Cheats: Base Building** (third tab; `BaseBuildingEngine`, `BaseBuildingService`, `BuildTemplateStore`, `Instruments/BaseBuilding.razor`).
  The owner places a foundation with a **beacon** (gId `Beacon`, `text` = "Fish") on it, turned to point the way a row should grow, saves,
  and picks it here; the tool builds a straight row of platforms out from it, each with the template's chests, each chest labelled
  and filtered with the next item of a recipe (Fish = `Fish1Eggs`..`Fish14Eggs` from the item table; also Frog, Butterfly, Tree). Facts
  read from Custom-1 (2026-09-26): a beacon stands 2.519 m above its foundation's position, so do chests; foundations sit 6 m apart on
  the axes; **direction = the opposite of Unity forward, (-sin yaw, -cos yaw), snapped to an axis** (a beacon at yaw 180 pointed +z = west,
  at yaw -90 points +x = north; north is +x, east is -z). **Templates** (what one platform of chests looks like: chest gId, slots, each chest's
  offset/height/rotation, and the direction it was captured in) are captured from a sample the owner builds (a beacon pointing at a platform
  with chests) and kept in `%LOCALAPPDATA%\RRSOS-PCC-Live\build-templates.json`, one per chest type; applying one to another direction
  turns the offsets and rotations by a multiple of 90°. The captured `Container1`: 4 chests (0.6, -1.4), (-0.6, -1.4), (0.6, 1.6), (-0.6, 1.6)
  in a +z row, alternating 0/180°. **Everything on the beacon's own platform and behind it is ignored; only what is ahead matters** (each
  planned platform is checked for existing pieces; a foundation already there is reused). Every platform gets the full template (14 fish =
  4 platforms, 2 spare unlabelled chests). Several beacons may share a name (a fish beacon per base): each is listed with the nearest known base and one
  is picked explicitly. **Build** writes new records with `SaveResupplyService.EditAsync` (backup, atomic write, signature check): a
  `Foundation` per new platform, each chest as `{id, gId, liId, liGrps, pos, rot, planet, text}` plus its own empty inventory
  `{id, woIds:"", size}` (with `demandGrps` = its item when "also set demand" is ticked, and, when "also fill each labelled chest" is ticked, `woIds` listing one new
  `{id, gId}` item record per slot, 15 per fish chest = 210 items for a fish row; the same shape Resupply's item-id fill writes). Save layout: sections joined by CR @ CR, objects first
  then inventories, records joined by `|` + newline; new objects go in front of the beacon's record, new inventories in front of the last
  inventory's; new object ids are random in 200M-210M, new inventory ids continue after the highest (the game now uses 100M+ for those).
  The proof is that removing exactly the added text gives back the original; the check judges only the new records, since real saves
  already have ids used twice. Tested on copies of Custom-1: 4 foundations + 16 chests + 16 inventories (+ 210 item records with fill) added, nothing else changed. The owner
  built a fish row into their real Custom-1 on 2026-09-26 (about 06:00) and it looked good in the game.
- **Object search** in the Notes card (`Instruments/ObjectSearch.razor`): a text box; typing shows matching objects'
  Name and gId (from the item catalog, `worldobjectdata.json`), by name or id, any case, spaces ignored, exact match
  first, 50 rows. Selecting a match adds a note "Name = gId": click a row, or Enter for the highlighted one (the first
  match; ↑/↓ moves it; Enter with no match does nothing). Only objects in that table are searchable, so a missing id
  there is missing here too.
- **Hand edit to Custom-1 (owner's request, 2026-09-24):** emptied all 62 storage chests (`Container1`/`Container2`,
  by the dashboard's nearest-base-within-100-m rule) of the base "Home" (pod 201600281): 55 held 1,400 items; the item
  records were deleted, not orphaned. Growers and machines were left alone. Backup: `save-backups\Custom-1.20260924-231731.json`.
  Done with a verified script (every other record byte-identical); the owner then cleared their Cheats configs
  (backup `resupply-configs.json.before-clear.bak`) to test the item-id fill.
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

Nothing is in progress. Open ideas (none asked for yet): more spoken alerts (an
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
