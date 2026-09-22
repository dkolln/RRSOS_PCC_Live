# Handoff: where things stand and what comes next

Written at the end of the 2026-09-21 sessions so a new session (or a new person) can pick up cleanly. The first session
built the player, planet, vehicle and dashboard modules; the second (later the same day) built **Base and Extractors**.

## Where everything is

| What | Where |
|---|---|
| This project (plugin and live dashboard) | `C:\Users\david\source\repos\Planet Crafter\RRSOS-PCC-Live`, GitHub `dkolln/RRSOS_PCC_Live` (public) |
| The save-file dashboard (stays save-only) | `C:\Users\david\source\repos\Planet Crafter\RRSOS-PCC`, GitHub `dkolln/RRSOS-PCC` |
| The game | `C:\Program Files (x86)\Steam\steamapps\common\The Planet Crafter` (Unity 6000.3.2, Mono, Steam build 25296421) |
| BepInEx 5.4.23.4 | installed in the game folder; details and undo steps in `install-log.md` |
| Save backups (taken before installing) | `%USERPROFILE%\Documents\PlanetCrafter-SaveBackups\2026-09-21_1317\` |
| The live files | `%LOCALAPPDATA%\RRSOS-PCC-Live\live.json` (once a second) and `live-world.json` (every 5 s); contract in `contract.md` |
| The dashboard's base names | `%LOCALAPPDATA%\RRSOS-PCC-Live\basedata.json` (its own file; RRSOS-PCC's is separate) |

Note that on this machine the repos live under `C:\Users\david` but the profile that owns `%LOCALAPPDATA%`, the saves
(`...\AppData\LocalLow\MijuGames\Planet Crafter`) and the game's `%USERPROFILE%` is `C:\Users\dkoll`.

Run it: build the plugin with the game closed (`dotnet build src/Live/Live.csproj`, it copies itself into
`BepInEx\plugins`), start the dashboard (`dotnet run --project src/Dashboard`, http://localhost:5320), launch the game.
Without the game, use `tools\sample-live.ps1` (fake data) or `tools\save-to-world.ps1` (a real save turned into the two files).

## What works

**Verified in the game (2026-09-21, plugin 0.2.0):** player position, yaw and vitals (with maximums), backpack, gear, six
planet stats with live per-second rates, power (produced, used, generators by kind), rocket counts and multipliers, and
the vehicle (position, trunk, gear). Rocket multipliers matched the wiki maths exactly, the vehicle position matched a
save, and the owner reviewed the dashboard live and was delighted. Later the same day the owner confirmed again that
player, vehicle, power and planet data "all look good".

**Built but NOT yet run in the game (plugin 0.3.0, this is what the next session must test): Base and Extractors.**

- **Plugin:** a second file, `live-world.json`, written about every 5 seconds by `WorldPoller` (a coroutine), using
  `WorldScan`, which walks the game's placed objects in three passes and spreads the work over frames (about 2 ms each). It reports **raw
  facts**: pods with their panels, signs, containers (with their items) within 120 m of a pod, loose items near a pod, and
  ore, gas, water and algae extractors (product, size, count, contents). It also reports its own cost (`scan`). See `contract.md`, "The world file".
- **Dashboard:** `WorldFileService` reads that file and works out the bases with `BaseDirectory` (a base is a pod with a door;
  door plus connection is a "Base", else an "Outpost"; things belong to the nearest base within 100 m; names from signs, then
  a saved name, then a pool: `BaseNames`). **One page, no tabs** (the owner's layout, after a first version with Main, Base and Extractors tabs): three columns, sized for 2560 x 1440.
  Left: **Player, with Vehicle under it**. Middle: **Planet**. Right: the **Base card** over the **Extractors card**. The Player card's top row is compass, elevation and the
  **Bases map** (`NavDisplay`'s `Extra` slot; its position map is switched off with `ShowPosition="false"`). Hover a base on the map for its name; click it to pin it (click again,
  or "Follow nearest", to go back; sticky nearest via `BaseSelector`). The Base card has three lists, **folded to begin with**, each showing 20 rows and then scrolling:
  Stored (by name), Ready to harvest and Boneyard. Backpack, gear and trunk can be folded too (`ItemList`'s `Collapsible`; they start open). The Extractors card groups ore by product,
  then gas, water and algae, with position, distance, direction, fill bar and contents, and scrolls.
- **Ported from RRSOS-PCC (copied, not referenced):** `BaseMap` (small version only now), `BaseContents` (built on the existing `ItemList`), `BaseSelector`,
  the extractor grouping, the item-type table (`Assets/worldobjectdata.json` and `ItemType`/`ItemCatalog`) and the two name pools.
- **Checked without the game:** the plugin compiles against the game's assemblies with no warnings. The dashboard was run against the fake world
  (`tools/sample-live.ps1`) and against **your real save `Custom-2`** through `tools/save-to-world.ps1`: 16 door pods found (1 Base, 15 Outposts), the same count as
  counting the save by hand, names identical to RRSOS-PCC's saved ones, 38 extractors, Main with 4049 stored items. The page fits 2560 x 1440 with nothing clipped (Player and Vehicle share the left column 59 to 41; the backpack shows in full).
- **What the save-based check cannot show:** whether the plugin's game calls return what the decompiled code suggests (see `test-plan.md` section 9.7,
  the list of assumptions), and how much the world pass costs in a real session (`scan.workMs` and `scan.worstFrameMs` in the file).

Not yet confirmed by the owner from earlier: the Energy Levels and Terraformation screen comparisons, the backpack contents,
the compass in all eight directions, whether a stowed truck's trunk and gear still show, and the long main-menu wait.
See `test-plan.md` (tick items off as they are done).

## Next

1. **Test section 9 of `test-plan.md` in the game** (the owner asked for a detailed list to work through on return). Start with 9.1
   (does the file appear, are the counts right, what does `scan` say). Anything that fails will most likely be one of the assumptions in 9.7.
2. Fix what the game shows to be wrong. The likely suspects, in order: pods missing (the "constructed objects" list or the planet hash), every
   pod an outpost (panel numbers), an empty "Ready to harvest" (secondary storage), ore groups all "Unset" (`GetLinkedGroups`), dropped items not listed as loose (the scene-id rule).
3. Decide a few small things the owner has not been asked yet:
   - **Base names:** the live dashboard keeps its own `basedata.json` (so it never depends on RRSOS-PCC). The bases with signs get the same names in both apps; unsigned ones get
     pool names that may differ. Seeding from RRSOS-PCC's file is possible (both key by the pod's game id) if the owner wants identical names.
   - Whether gas extractors, the genetic extractor and the water-life collector belong on the Extractors card (gas is in; the other two are not).
   - **Far bases cannot be picked:** the Bases map only reaches half the range that would hold every base, and it is now the only way to choose a base. If that bites, options are a wider map or a small list of bases.
   - Removed on request when the layout changed: the big map and its zoom, the Group by Name/Type/Category buttons, and the search box. The code for grouping by type is gone with them (`ItemCatalog` is still used to leave machines and building parts out).
4. Then module 8 (optional): drones, a local HTTP feed, an in-game overlay.

## Working rules that emerged (from the owner)

- Read-only, awareness not shortcuts. Never change game state.
- RRSOS-PCC stays save-file based; do not make the two depend on each other.
- Ask before committing or pushing in RRSOS-PCC. In this repo the owner asked for local commits per module and pushed on request.
- Changing the game folder (installing or updating BepInEx or the plugin) needs the game closed, and the owner's go-ahead for anything new.
  (The 0.3.0 plugin DLL was built into `BepInEx\plugins\RRSOS-PCC-Live` at the end of the second session, with the game closed, the same routine as before.)
- Test the dashboard against fake data, then hand the owner a precise test list for the game (as in `test-plan.md`).
- The owner's saves: `Custom-1.json` (newer, no vehicle yet) and `Custom-2.json` (older, mid-game, now flagged `modded`). Never edit them
  (`save-to-world.ps1` only reads).

## Facts worth remembering

- Compass: north is world +X, east is world -Z (measured in the game). `Compass.ToEastNorth` in the dashboard, `PCMath.ToEastNorth` in RRSOS-PCC.
- The game marks saves `modded: true` while `BepInEx\plugins` is non-empty. Cosmetic.
- Fandom wiki pages cannot be fetched by tools (HTTP 402) but open fine in a browser; the wiki's tables match the game.
- The vehicle is the world object with group id `VehicleTruck`; a stowed vehicle has no position.
- Object ids: below 200,000,000 is the landscape's own; things the player builds or drops are 200,000,000 and up. Panel value 4 is a door, 2 a corridor connection.
- A shell quirk on this machine: a long `bash` heredoc with several files in one command can fail to parse; write files one at a time.
- To look at the dashboard while developing: the launch config in `.claude/launch.json` (ignored by git) starts it on fake or converted data; regenerate the decompile
  with `ilspycmd -p -o <a scratch folder> "<game>\Planet Crafter_Data\Managed\Assembly-CSharp.dll"` (about 30 seconds, kept out of the repo).
