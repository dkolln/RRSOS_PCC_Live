# Handoff: where things stand and what comes next

Written at the end of the 2026-09-21 session so a new session (or a new person) can pick up cleanly.

## Where everything is

| What | Where |
|---|---|
| This project (plugin and live dashboard) | `C:\Users\david\source\repos\Planet Crafter\RRSOS-PCC-Live`, GitHub `dkolln/RRSOS_PCC_Live` (public) |
| The save-file dashboard (stays save-only) | `C:\Users\david\source\repos\Planet Crafter\RRSOS-PCC`, GitHub `dkolln/RRSOS-PCC` |
| The game | `C:\Program Files (x86)\Steam\steamapps\common\The Planet Crafter` (Unity 6000.3.2, Mono, Steam build 25296421) |
| BepInEx 5.4.23.4 | installed in the game folder; details and undo steps in `install-log.md` |
| Save backups (taken before installing) | `%USERPROFILE%\Documents\PlanetCrafter-SaveBackups\2026-09-21_1317\` |
| The live file | `%LOCALAPPDATA%\RRSOS-PCC-Live\live.json` (contract in `contract.md`) |

Run it: build the plugin with the game closed (`dotnet build src/Live/Live.csproj`, it copies itself into
`BepInEx\plugins`), start the dashboard (`dotnet run --project src/Dashboard`, http://localhost:5320), launch the game.
Without the game, use `tools\sample-live.ps1`.

## What works (verified in the game on 2026-09-21)

Plugin 0.2.0 and the dashboard were run against the real game and everything arrived, with no plugin warnings:
player position, yaw and vitals (with maximums), backpack, gear, six planet stats with live per-second rates, power
(produced, used, generators by kind), rocket counts and multipliers, and the vehicle (position, trunk, gear).
Rocket multipliers matched the wiki maths exactly, and the vehicle position matched a save. The user reviewed the
dashboard live and was delighted with it.

Not yet confirmed by the user: the Energy Levels and Terraformation screen comparisons, the backpack contents, the
compass in all eight directions, whether a stowed truck's trunk and gear still show, and the long main-menu wait. See
`test-plan.md` (tick items off as they are done).

## Next: Base and Extractors

The user's words: **"base data, base maps (both main and tab page) and extractor data."** That means, for the live
dashboard:

1. **Base data from the plugin**: where the bases are and what each holds.
2. **Base maps, two of them**, as in RRSOS-PCC:
   - a **compact map on the Player card** (Main page): you in the middle, nearby bases around you, hover shows a name under the map, clicking a base opens the Base tab with it selected;
   - the **big map on a Base tab**: every base, zoom and Fit, click to pin a base, "Follow nearest" to go back, with the base's contents beside it.
3. **Extractor data** and an Extractors tab: each extractor's product, fill level, position and direction.
4. **Tabs** on the dashboard: Main, Base, Extractors (as in RRSOS-PCC). Inactive tabs are hidden, not removed, so their state survives.

Constraint from the user: the Main page must still fit a 2560 x 1440 screen with no scrolling.

### What to port from RRSOS-PCC (copy, do not reference)

- `Components/Pages/BaseMap.razor(+css)` (the map, with `Compact` mode; already uses the correct compass), `BaseContents` (and its builder) and `BaseSelector.cs` (the sticky "nearest base" logic with pinning).
- The extractor view models (`ExtractorSummaryVM`, group and subgroup VMs) and the Extractors card.
- How the save-based app decides what a base is: `Classes/ProcessBinder.cs`, `BindBases` (about line 476) and `PCMath.FindNearestBase` (objects belong to the nearest base within 100 m). A **base is a living compartment (pod) that has an Entrance panel (a door)**; outposts are the rest. Names come from signs, else a procedural pool (`BaseNamingService`, saved in `%LOCALAPPDATA%\RRSOS-PCC\basedata.json`). Decide whether the live dashboard reads that same names file, or keeps its own.

### Things to find out in the game's code first (module 2 style)

Regenerate the decompile (about 30 seconds, kept OUT of the repo):
`ilspycmd -p -o <a scratch folder> "<game>\Planet Crafter_Data\Managed\Assembly-CSharp.dll"` (`ilspycmd` is a global .NET tool).

- How pods and their panels are represented: `WorldObject.GetPanelsId()`, the panel object's group and module, how a door/Entrance panel is recognised. Look at `Pod`, `Panel*` classes and `WorldObjectsHandler.GetConstructedWorldObjects()`.
- How extractors expose their storage: the machine's linked inventory (`GetLinkedInventoryId()` and `InventoriesHandler.GetInventoryById`), its capacity (`Inventory.GetSize()`), what it produces, and whether it is running. RRSOS-PCC's `OreExtractor` model and `ProcessBinder` (extractors, water collectors, algae, and so on) show which group ids count.
- What signs say: a sign's text is `WorldObject.GetText()`; base names come from there.
- Whether an object's position is live: use `GetGameObject().transform.position` when it exists, otherwise `GetPosition()`.

### Design points to decide

- **Payload size and speed.** The game has thousands of world objects. Bases and extractors change slowly, so do not put them in the once-a-second file. Suggested: a second file (for example `live-world.json`) written every 5 to 10 seconds, with the fast file unchanged. The dashboard watches both.
- **Where the grouping happens.** Simplest and lightest: the plugin sends raw facts (pods with position and door flag, extractors with position and contents, containers with position and contents, sign texts) and the dashboard groups them into bases, as RRSOS-PCC does. That keeps the plugin small and the logic testable in the dashboard with fake data.
- **Base contents** (what is stored where) means every container's items. Decide whether to send them all, or only the selected base's (the dashboard would have to tell the plugin, which breaks read-only; so send all, but compact: counts by kind per container).
- **Cost on the game's main thread.** Reading runs in the game's update loop. Keep each read cheap, spread heavy ones over frames, and measure a frame time.
- Keep the rule: **every section is read on its own and may fail to null**; add an item to `test-plan.md` for everything new.
- Extend `tools/sample-live.ps1` with world data (bases, extractors) so the dashboard can be built and checked without the game.

## Working rules that emerged (from the owner)

- Read-only, awareness not shortcuts. Never change game state.
- RRSOS-PCC stays save-file based; do not make the two depend on each other.
- Ask before committing or pushing in RRSOS-PCC. In this repo the owner asked for local commits per module and pushed on request.
- Changing the game folder (installing or updating BepInEx or the plugin) needs the game closed, and the owner's go-ahead for anything new.
- Test the dashboard against fake data, then hand the owner a precise test list for the game (as in `test-plan.md`).
- The owner's saves: `Custom-1.json` (newer, no vehicle yet) and `Custom-2.json` (older, mid-game, now flagged `modded`). Never edit them.

## Facts worth remembering

- Compass: north is world +X, east is world -Z (measured in the game). `Compass.ToEastNorth` in the dashboard, `PCMath.ToEastNorth` in RRSOS-PCC.
- The game marks saves `modded: true` while `BepInEx\plugins` is non-empty. Cosmetic.
- Fandom wiki pages cannot be fetched by tools (HTTP 402) but open fine in a browser; the wiki's tables match the game.
- The vehicle is the world object with group id `VehicleTruck`; a stowed vehicle has no position.
