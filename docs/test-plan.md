# Test plan

> **Status (2026-09-21):** this plan was written before the game could be run. The first real run has since happened:
> every section arrived with no plugin warnings, the rocket multipliers matched the wiki maths, the vehicle position
> matched a save, and the dashboard looked right against real data. Items below are still worth ticking off
> individually (many need a specific action in the game); untested ones are the open questions.
>
> What was tested without the game: the plugin compiles against the game's real assemblies, and the dashboard was
> exercised against a fake file (`tools/sample-live.ps1`) in every state at 2560 x 1440.
>
> **Added later the same day: plugin 0.3.0 with bases and extractors (section 9).** None of that has run in the game.
> The owner confirmed that player, vehicle, power and planet data "all look good" in the game before this was added.
> **Start with section 9, then section 10 (drones)**, then tick off the older leftovers (sections 3 to 6) as you meet them.

Work top to bottom. Each item says what to do and what should happen. Tick them off; anything that does not match is
a bug or a wrong assumption, so write down what you saw. A few PowerShell snippets are at the end.

## 0. Before you start

- [ ] Game is closed. Steam is running.
- [ ] `BepInEx\plugins\RRSOS-PCC-Live\RRSOS.PCC.Live.dll` exists in the game folder and is newer than your last game session.
  If not, run `dotnet build src/Live/Live.csproj` in this repo (the game must be closed; a loaded plugin file is locked).
- [ ] Start the dashboard: `dotnet run --project src/Dashboard`, then open http://localhost:5320.
- [ ] Your saves are backed up (`Documents\PlanetCrafter-SaveBackups\...`). Playing with the plugin flags a save `modded`
  while `BepInEx\plugins` is non-empty (cosmetic; see `game-notes.md`).

## 1. Start-up order (the question "what launches first?")

The answer is meant to be "either". Test all four:

- [ ] **App first, game closed:** the page shows `WAITING FOR THE GAME` and the Launch PC button is enabled.
- [ ] Click **LAUNCH PC**: Steam starts the game. The button changes to `PLANET CRAFTER RUNNING` (disabled) within
  about 2 seconds of the game process appearing.
- [ ] While the game sits on the main menu: status `GAME AT THE MAIN MENU`. **Leave it there for a full minute:** it must stay that way (the plugin writes the menu state only once, so the page has to notice the game is still running; it must not turn into `GAME NOT RUNNING`). Load a world: it turns `LIVE` within a couple of seconds.
- [ ] Load a save and watch the loading screen: the page may briefly say `NO RECENT DATA FROM THE GAME` (the plugin is quiet while loading); it should return to `LIVE` by itself.
- [ ] **Game first, then the app:** start the game and load a world, then start the dashboard. It should show `LIVE` almost immediately.
- [ ] **Launch PC when already running:** the button is disabled. (It must never close or restart the game.)
- [ ] **Steam not running:** click Launch PC. Steam should start and then the game; if nothing happens, note it (the launcher just opens `steam://rungameid/1284190`).

## 2. Plugin log and file (in the game)

Open `BepInEx\LogOutput.log` after a session.

- [ ] Line `RRSOS PCC Live 0.3.0 loaded. Read-only ...` appears.
- [ ] Line `In world: Prime. Writing ...\live.json once a second.` appears when you enter a world.
- [ ] Line `Not in a world. The live file now says so.` appears when you leave to the menu.
- [ ] No lines starting `Could not read '...'`. If there are, note which section: each means that section arrives as
  `null` (the page shows "no data" for it) and it names the game call that failed.
- [ ] Also fine and expected: `[Error] Unable to start Unity log writer`, and the red "Mods Detected" notice in the game.
- [ ] `%LOCALAPPDATA%\RRSOS-PCC-Live\live.json` updates about once a second, is valid JSON on every read, and has
  `"schemaVersion":1`. (Snippets at the end.)
- [ ] Quit to the menu: the file gets `"inWorld":false` and stops changing. Load a world again: it resumes.
- [ ] Close the game **from inside a world** (quit from the pause menu, or Alt+F4): `inWorld` should end up `false`
  (the quit handler). If it stays `true` with an old `updatedAt`, the dashboard still shows `GAME NOT RUNNING` after 6 s, which is acceptable.

## 3. Player card

Already verified in an earlier build: position, yaw and vitals (the position matched a save to four decimals).

- [ ] **Elevation:** the altimeter number matches what you expect and changes smoothly as you climb. (The Player card no longer shows a position map or coordinates; the position is in the file, and the Bases map is where you are.)
- [ ] **Compass:** face each of N, NE, E, SE, S, SW, W, NW using the **in-game compass**. The heading dial should agree
  each time. (North is world +X, east is world -Z; measured, but only with two runs.)
- [ ] **Vitals:** the four dials read sensibly. **Oxygen max** should be your tank's size (was 370 with the current
  gear): swap to a smaller or larger tank and the dial's right-hand label and fill should follow. Toxicity is 0 outside toxic areas.
  If the max could not be read (log line `Could not read _oxygenMaxValue`), the dial falls back to 100.
- [ ] **Backpack:** the list matches your real backpack: same item kinds and counts, **display names** as the game shows
  them (localized text), not raw ids. Pick up an item, drop one, craft one: the list follows within about 2 seconds.
  The header shows `items / capacity`.
- [ ] Items whose name looks odd (an id such as `Rod-iridium` instead of a name) mean the game had no localized name.
  Hover an item to see its id.
- [ ] **Gear:** boots, jetpack, multitool, tank and the chips (compass, map, ...) appear. Swap one and it updates.
- [ ] A very full backpack (100+ kinds) still fits on screen (the list clips rather than pushing the layout).

## 4. Planet card

- [ ] **Six TI dials** (Oxygen, Heat, Pressure, Plants, Insects, Animals): values match the game's Terraformation screen
  or your last save (the numbers are the same units, shown as e.g. `1581.82B`).
- [ ] **Rates (`▲ .../s`):** the per-second gain. Compare with the game's own Terraformation screen (it shows the rate of
  increase per stat). They should match to the displayed precision. Build or remove a machine and the rate should change
  within a couple of seconds.
- [ ] **Rockets (`🚀 n · x m`):** the count per stat matches your launched rockets (the Orbital Information screen lists
  them). With T1 rockets only, the multiplier should be 10 per rocket (3 Heat rockets = x30), Plants 12.5, Insects 15,
  Animals 17.5. **Launch one more rocket:** count and multiplier update. This is the number the game itself uses.
- [ ] **Power:** the dial's load, `▲ produced` and `▼ used` match the game's **Energy Levels screen**. `Left`/`Short`
  is produced minus used. Add or remove a machine (or a generator): all of them update.
- [ ] **Generator icons:** one icon per generator, grouped by kind, wind and solar on the left of the dial and nuclear
  and fusion on the right. The counts match what you have built. Hover a group for its live output (`Solar Panel T2 x9: ... kW`).
  Machine Optimizer boosts should show as a higher kW for boosted generators.
- [ ] **Generator kinds you have never placed** (T2 wind turbine, wreck reactors): they may show as a plain bolt with the
  raw id. Note any id you see so it can be given a name and icon.
- [ ] Power with the world's **power-consumption setting** not at 100%: `used` should already reflect it (the game applies it).

## 5. Vehicle card

- [ ] **Deployed truck:** position appears on the small map relative to you (amber square), with `Vehicle n m <dir>`
  underneath and the right compass letter. The heading dial shows the truck's own heading, the altimeter its height.
  Walk away and the square moves and the range steps up (50, 100, 250, ... m).
- [ ] **Driving it:** position and heading follow the truck, and the map centre (you) moves with it.
- [ ] **Trunk:** matches the truck's real storage (kinds, counts, names). Put something in, take something out: it follows.
- [ ] **Gear:** the vehicle modules (beacon, lights, equipment size, oxygen, inventory size, speed) match.
- [ ] **Stowed in your pocket:** the map is dimmed, no square, caption `no position (pocket or portal)`, the coordinates line
  says `stowed`. **Do the trunk and gear lists still show while stowed?** That is a question, not an expectation: the
  inventories should still exist, but if they show `no data`, note it.
- [ ] **Truck in a portal / inside another instance:** same as stowed (no position). Note what the file says.
- [ ] **Recall to the garage** (the game's back-to-garage action): the position should jump once and then track normally.
- [ ] **No truck yet** (your newer save): the card says `No vehicle yet.`
- [ ] The vehicle is found by the group id `VehicleTruck`. If your game has a different vehicle type, it will not appear.

## 6. Dashboard (layout and behaviour)

- [ ] At **2560 x 1440, browser zoom 100%**: all three cards fit with nothing scrolling and nothing cut off, even with a
  full backpack. (Browser full screen, F11, is the intended way to run it.) There are no tabs any more: the page is one screen (Player over Vehicle, Planet, Base over Extractors; section 9.3).
- [ ] Other sizes (1920 x 1080): note what breaks; the layout is designed for 2K and is not responsive yet.
- [ ] **Stale detection:** close the game with the dashboard open: within about 6 seconds the status becomes
  `GAME NOT RUNNING - showing the last reading, from HH:MM:SS`, the data dims but stays visible.
- [ ] **Menu:** quit to the main menu: the cards say `No world loaded.`
- [ ] Reload the browser tab, or open a second tab: both show the same live data.
- [ ] Leave it running an hour: no growth in memory or CPU (Task Manager, `RRSOS.PCC.Dashboard`), no log spam in the console.
- [ ] Only one menu item exists (Launch PC). There is no way to change anything in the game from the page.

## 7. Game side effects (there should be none)

- [ ] Play 30 minutes with the plugin: no stutters when it writes (once a second), no crashes, no errors in the game's own
  `Player.log` mentioning `RRSOS`.
- [ ] **Saving:** save the game while the plugin runs; the save loads normally. The save has `"modded":true` (expected).
- [ ] **The plugin never writes to the game.** Confirm no file in the game's save folder changes except when *you* save.
- [ ] Uninstall works: empty `BepInEx\plugins` (or follow `install-log.md`), start the game, save: the save is `"modded":false` again.
- [ ] After a **game update** (the Steam build id changes from 25296421): if the plugin misbehaves, `game-notes.md` says where to start.

## 8. Things I could not check and assumed

These are the likely places for surprises. Each is a guess, made from reading the decompiled game:

1. **Rocket multipliers** come from constructed world objects whose group multiplies a unit (the hidden `SpaceMultiplier*`
   objects). If the rocket line is missing or the multiplier looks wrong, the game keeps them somewhere else.
2. **Generator list** counts every constructed object with positive Energy output. It does not filter by planet (fine on
   one planet; wrong once you have two).
3. **`Energy` increase and decrease** are treated as produced and used. If the Power card disagrees with the Energy Levels
   screen, check the sign or scale here first.
4. **The vehicle** is the world object with group id `VehicleTruck`; "out in the world" means its scene object exists and
   is active. Stowed or portal states may behave differently than assumed.
5. **Tier 2 rockets** have unknown ids. The old dashboard assumed an id ending in `2`; this plugin does not care (it reads the
   game's multiplier), but the count comes from the items in the space container, so it should be right either way.
6. **Inventory display names** use `Readable.GetGroupName`. If it throws or returns nothing, the id is shown.
7. **Gauge maximums** are read with reflection from private fields; a game update that renames them makes the dials fall back to 100.
8. **Multiplayer** is unsupported: only the local player is read.
9. Bases and extractors are in the file (section 9), and drones (section 10).

## 9. Base and Extractors (plugin 0.3.0; on the Main page: the Bases map, the Base card and the Extractors card)

**Nothing in this section has been run in the game yet.** It was built from the decompiled game code and checked against
fake data and against your real save `Custom-2` (converted with `tools\save-to-world.ps1`): 16 door pods found, 1 base and
15 outposts, the same numbers as counting the save by hand, and the base names came out the same as RRSOS-PCC's. What
remains is everything that only the running game can tell: whether the plugin's reads work, and whether the numbers match
what you see. Run it in a world with a few bases, some extractors and a grower, ideally the newer of the two saves, and
have RRSOS-PCC open on a save made a moment before for comparison.

Before the game: the plugin DLL in the game folder was rebuilt at the end of the 2026-09-21 session and should say 0.3.0 in
the log. If you change anything, rebuild (`dotnet build src/Live/Live.csproj`, game closed).

### 9.1 The world file

- [ ] `BepInEx\LogOutput.log` says `RRSOS PCC Live 0.3.0 loaded` and names **both** files (`live.json` and `live-world.json`).
- [ ] After you load a world, `%LOCALAPPDATA%\RRSOS-PCC-Live\live-world.json` appears within about 10 seconds and is rewritten about every
  5 to 6 seconds (use snippet A below). It is valid JSON on every read.
- [ ] **No lines in the log** starting `Could not` that mention the world: `Could not list the game's objects`, `Could not read a placed object`,
  `Could not read a container`, `Could not read a loose item`, `Could not write the world file`, `Could not read the planet's hash`.
  If there are, note the text: each names the game call that failed (and only the first three of each are logged).
- [ ] **The counts look right** (snippet A): `pods` roughly equals the number of compartments you built (plus your bigger `Pod4x`/`Pod9xC` modules, which
  are in the file but are not bases); `signs` equals your signs; `extractors` equals your ore, gas, water and algae machines; `containers` is not empty.
  **If `pods` is 0 in a world with bases, the planet-hash filter or the "constructed objects" list is the suspect** (say so).
- [ ] **Cost to the game.** Snippet A prints `scan`: `worstFrameMs` should be a few milliseconds at most (the target is about 2) and `workMs` a
  few tens. Play a few minutes in a big base (fast movement, flying the jetpack): **no stutter every 5 seconds.** Note the numbers even if it feels fine.
- [ ] The file is a sensible size (snippet A prints it). Under about 500 KB is expected; over 1 MB means something is being listed that should not be.
- [ ] Quit to the main menu: the file gets `"inWorld":false` once and stops changing. Load a world again: it resumes.
- [ ] Load a save, quit **without** saving: your save files are untouched, as before.

### 9.2 Bases: what is a base

Compare with RRSOS-PCC on the same save (it decides the same way: a pod with a door; door plus connection is a Base).

- [ ] The Base card title `n bases, m outposts` matches RRSOS-PCC's Bases list for a save made a moment ago.
  Known, intended differences: this counts a door pod called `EscapePod` if it had a door (RRSOS-PCC's match misses that spelling; yours has no panels, so none today),
  and the larger `Pod4x`/`Pod9xC` modules are not bases in either.
- [ ] **Base versus Outpost:** a pod with a door and a corridor connection is drawn as a diamond and called `base`; a door only is a dot and `outpost`.
  Connect a corridor to a door-only outpost: within about 10 seconds it becomes a base.
- [ ] **Names:** a base with a sign next to it (within about 6 metres) shows the sign's text. Bases without a sign get a name from a list (Greek names for bases, NATO letters for outposts).
  Stop and restart the dashboard: **the same bases have the same names** (they are saved in `%LOCALAPPDATA%\RRSOS-PCC-Live\basedata.json`).
  Put a sign by an unnamed base: it takes the sign's name. Change the sign's text: the name follows.
- [ ] **New and removed bases:** build a new door pod: it appears within about 10 seconds. Deconstruct one: it disappears from the map, and if you were pinned to it the Base card goes back to the nearest one.
- [ ] Positions: the pip for a base you are standing next to is in the middle of the Bases map, on top of you.

### 9.3 The layout (one page, no tabs) and the Bases map on the Player card

The page is three columns: **Player with Vehicle under it**, then **Planet**, then **Base and Extractors** stacked. There are no tabs.

- [ ] **The whole page fits 2560 x 1440 with no scrolling** (browser at 100%, F11) and nothing is cut off, with a full backpack. Only the lists inside cards scroll.
- [ ] The Player card's top row is the compass, the elevation strip and the **Bases map** (the position map and the coordinates are gone; the Vehicle card still has its own map of where the truck is).
- [ ] **Backpack** (33 or more kinds) shows in full, or scrolls inside its own area. Vehicle **trunk** the same. A trunk with 30 kinds should scroll rather than push anything off the page.
- [ ] Click the title of Backpack, Gear or Trunk: it folds away and back. (They start open. Say if you would rather they start folded.)
- [ ] The Bases map: you in the middle, north up, the light cone shows which way you face (turn and it follows). The label at its bottom right (`250 m`...) is its range. Bases farther than that are left off it.
- [ ] **Hover** a base: its name appears under the map with distance, direction and whether it is a base or an outpost. Move away: it shows the base the Base card is showing.
- [ ] **Click** a base: the Base card switches to it and shows a lock and **Follow nearest**. Click the same base again, or the button: back to the nearest.
- [ ] Walk between two bases: the pips slide, the compass letters in the caption (`120m NE`) agree with the in-game compass, and the Base card does not flip back and forth (it changes only when another base is 15 m closer).

### 9.4 Base card (top right)

The card names the base (`Main`, `120m NW`, `base` or `outpost`) and has three lists, **all open on load**: `Stored`, `Ready to harvest` and `Boneyard (loose)`
(the last two side by side). Each shows 10 rows and then scrolls; click a title to fold it away. Pick a base you know well (a room with a few chests).

- [ ] **Stored** lists what is in the base's containers, by the game's own (localized) names. **Add 5 Iron to a chest in that base: within about 10 seconds Iron goes up by 5.** Take some out: it goes down.
- [ ] Items in **machines** with storage (auto-crafters, incubators, growers) are counted as stored too (RRSOS-PCC does the same).
- [ ] Nothing that is a **machine or a building part** is listed as stored (no `Foundation`, no placed chests).
- [ ] **Ready to harvest:** plants that have finished growing in a Vegetable Grower or Outdoor Farm, by name. Harvest one: the count drops. Plants still growing are not listed here (but do count under `Stored`, as in RRSOS-PCC).
- [ ] **Boneyard (loose):** it shows **loose raw material only**: ore (iron, cobalt, titanium, silicon, magnesium, iridium, aluminium, uranium, sulfur, obsidian, zeolite, osmium, ice), super alloy, quartz and rods. Drop 3 Iron on the floor inside a base: within about 10 seconds `Iron 3` (or more) appears here. Pick them up: gone.
  Plants growing outdoors, drones, vehicles, wheat, cocoa, algae and the like must **not** appear. The landscape's own rocks and the loot in wrecks must not either.
  Anything the table does not know is left out; note any material you see lying around that is missing.
- [ ] The three lists scroll after 10 rows, and the number on each title is the total item count.
- [ ] **Range:** something in a chest more than 100 metres from every base is listed nowhere (it belongs to no base). Something 60 m from base A and 90 m from base B belongs to A.
- [ ] Launched rockets (the hidden storage far away) never show up in any base.
- [ ] Compare the Stored list with RRSOS-PCC's for the same base on a fresh save. Small differences (a few items) are expected while things move; large ones are not, so note the base and the item.
- [ ] The list of bases is only reachable through the Bases map, which shows half the range that would hold every base. **A base too far to be on that map cannot be picked.** Tell me if that is a problem in practice.
- [ ] (Removed on request: the big map, the Group by Name/Type/Category buttons and the search box.)

### 9.5 Extractors card (under the Base card)

The card's title shows how many it found (ore, gas, water and algae machines; RRSOS-PCC does not show gas ones). It takes the rest of the column and scrolls.

- [ ] **Ore extractors** are grouped under the ore each one **is set to mine**, with a header `n items x/y full` per ore. Change one extractor's ore in the game: it moves to the other group within about 10 seconds.
- [ ] Each machine shows `X ... Z ...` (matches its position: compare with the coordinates you see standing next to it), **distance and direction from you** (updates as you walk, every second), a **fill bar** `count / slots`, and its contents.
- [ ] **Fill level:** the count matches what the machine's own screen shows; `slots` matches its capacity. A machine whose storage is full has a gold outline and counts under `full`.
- [ ] **Water collectors** (`Water Bottle`), **gas extractors** (their capsule) and **algae generators** are their own groups. For algae the bar counts only algae that have **finished growing** (`5 / 8 grown`) and the line says how many are ready.
- [ ] An empty machine says `no contents` and an empty bar.
- [ ] **Ore, Gas, Water and Algae start open; the individual ores (and gases) under them start folded**, showing only `n items x/y full`. Click an ore's header to open its machines, or a kind's header to fold it; what you open or fold stays that way while data refreshes.
- [ ] Extractors on another planet, and kinds not listed here (the genetic extractor, the water-life collector), are not shown. Note if you have any and want them.

### 9.6 Dashboard behaviour

- [ ] Close the game with the dashboard open: within about 6 seconds everything dims but keeps its last data, and the status says `GAME NOT RUNNING`.
- [ ] Quit to the main menu: the Base and Extractors cards say `No world loaded.` (or `Waiting for live data.`), not an error.
- [ ] Open the dashboard **before** the game, and after it: both work (as in section 1).
- [ ] Keep the dashboard open for an hour while playing: no growth in its memory (Task Manager), no warnings in its console window. (The plugin's file is rewritten every 5 seconds; the dashboard re-reads it when it changes.)
- [ ] Which lists you have folded or opened survives new readings (they must not spring shut every second).

### 9.7 What I assumed (the likely places for surprises)

1. **`GetConstructedWorldObjects()` contains the pods, signs, extractors and chests** (the game adds everything that is a building, or a buildable item, that is not from the scene). If pods are missing, this is why.
2. **`GetIsPlaced()` (position not zero) means "in the world"**, and the planet hash of a placed object equals the current planet's hash (the game compares them the same way).
3. **The pod's `GetPanelsId()` holds `BuildPanelSubType` numbers, 4 for a door and 2 for a connection**, exactly as the save's `pnls`. If every pod comes out as an outpost or no pod as a base, check the `panels` in the file (snippet B).
4. **A grower's plants are in its secondary storage** and finished ones have growth 100. If `Ready to harvest` is always empty, look at `containers` entries for your growers in the file (`secondary` should hold the plants).
5. **Ore and gas extractors name their product in `GetLinkedGroups()`** (the save's `liGrps`). If the ore groups all say `Unset`, that call is empty in the running game.
6. **Algae generators keep their algae in the secondary storage**, water and ore machines in the main one (as the save's `siIds` and `liId`).
7. **Objects with an id below 200,000,000 are the landscape's**, so they are not loose items. If your own dropped items never show as loose, this rule is wrong.
8. **A container is anything constructed and placed that has storage**; a sign is a group called `Sign`. Anything else that has storage (a machine) counts as a container too.
9. **Base logic** (what is a base, names, the 100 m ownership, ready to harvest) was **ported by reading** RRSOS-PCC, and checked against a real save, but not against a running game.

## 10. Drones (plugin 0.3.0: `drones` in `live.json`, `droneStations` in `live-world.json`)

**Not run in the game yet.** Built from the decompiled code (`Drone`, `MachineDroneStation`, `LogisticTask`) and checked on fake data only. Needs a world with at least one
drone station and a few drones working (an auto-crafter or a grower being fed from a container is enough to keep them busy).

### 10.1 The data

- [ ] No lines in the log like `Could not read a drone` or `Could not read a drone's cargo / task item / task source / task target`. If there are, note the text.
- [ ] Snippet D below shows `drones.flying` with one entry per drone that is **in the air** right now (docked ones are not listed there), each with a position that matches where you see it.
- [ ] The count of flying drones goes up when drones leave a station and down when they go home. (New drones can take up to about 5 seconds to appear: the plugin re-lists them every 5 seconds and reads positions every second.)
- [ ] `live-world.json` has `droneStations`: one per station, with its position, `size` (slots), `docked` (drones inside it) and `items`.
- [ ] Nothing is wrong with the rest: the file size and `scan.worstFrameMs` are about what they were (section 9.1).

### 10.2 The drone map (Player card, next to the Bases map)

- [ ] It is the last instrument in the Player card's top row, labelled `DRONES`. **The row still fits on one line at 2560 x 1440** and the whole page still fits without scrolling.
- [ ] You in the middle, north up, the cone shows which way you face. Drone **stations** are teal squares; drones **in the air** are small arrows pointing the way they fly, **amber when carrying something**.
- [ ] The scale (bottom-right, `100 m`, `250 m`, `500 m`, `1000 m`, `2000 m`) steps up to hold the closest station or drone. Anything farther than the edge is left off; with nothing within 2 km the map is empty but the caption still counts them.
- [ ] Drones **move on the map** as they fly (they glide between the once-a-second readings) and their arrows turn to match. Stand next to a station and watch one leave: it appears at the station and heads off in the right direction.
- [ ] The caption reads `n flying` and `n stations, n docked`. Hover a square or arrow for its name, distance and direction.
- [ ] **Click the map:** the **Drones** card opens under the Power card (the Planet card gets shorter, nothing else moves), and the map is outlined. Click it again: the card closes and the Planet card is full height again. Whether it is open survives new readings.

### 10.3 The drone card

- [ ] **Flying:** one tile per drone in the air, nearest first: its name (tier), what it is doing (`heading to pick up`, `delivering`, `loading`, `unloading`, `returning to a station`, `waiting for a task`), its distance and direction from you,
  what it is **carrying**, its **task** (`item from A (distance) to B (distance)`), its speed and height. Compare with the game's own logistics screen and with watching the drone.
- [ ] A drone with cargo has an amber outline; the `Carrying` line lists the items.
- [ ] **Stations:** one tile per drone station, nearest first: its position (`X ... Z ...`) which matches where the station stands, its distance and direction, a bar of `n docked / slots`, and its storage (the docked drones by tier).
  Send a drone out and back: `docked` goes down and up within about 5 seconds.
- [ ] The card scrolls if there are many drones; nothing is cut off silently.
- [ ] With no drones or no stations the card says so instead of showing an empty box.

### 10.4 What I assumed

1. **`Object.FindObjectsByType<Drone>()` finds the drones that are in the air**, and a drone put away in a station is an inactive object (so it is not listed). If docked drones show up as flying, or flying ones do not show at all, this is why.
2. **`Drone.transform.position` is the live position** and `forwardSpeed` is its speed in metres per second.
3. **A drone's `GetLogisticTask()`** is null when it has no job (shown as "returning to a station"); otherwise the task's supply and demand world objects are the machines it goes between.
4. **Stations have group ids starting `DroneStation`** (as in your save: `DroneStation1`) and their storage holds the docked drones as items whose group id is `Drone` plus digits (`Drone2`).
5. Because stations are now reported on their own, **their storage no longer counts as "stored" in the Base card** (docked drones used to be counted there as items).

## Snippets

Watch the live file (Ctrl+C to stop):

```powershell
while ($true) { $j = Get-Content "$env:LOCALAPPDATA\RRSOS-PCC-Live\live.json" -Raw | ConvertFrom-Json; "{0}  inWorld={1}  pos=({2:N1}, {3:N1}, {4:N1})" -f $j.updatedAt, $j.inWorld, $j.player.position.x, $j.player.position.y, $j.player.position.z; Start-Sleep 1 }
```

Which sections arrived (a `null` means that section failed to read):

```powershell
$j = Get-Content "$env:LOCALAPPDATA\RRSOS-PCC-Live\live.json" -Raw | ConvertFrom-Json
"player=$($null -ne $j.player) planet=$($null -ne $j.planet) vehicle=$($null -ne $j.vehicle)"
"backpack items=$($j.player.backpack.items.Count) gear=$($j.player.equipment.items.Count) power=$($j.planet.power.producedKw)/$($j.planet.power.usedKw)"
```

Plugin problems only:

```powershell
Select-String -Path "C:\Program Files (x86)\Steam\steamapps\common\The Planet Crafter\BepInEx\LogOutput.log" -Pattern 'RRSOS.*(Could not|Warning)'
```

Try the dashboard without the game: `tools\sample-live.ps1 -Path .\sample\live.json -Loop`, then
`dotnet run --project src\Dashboard --LiveFile=.\sample\live.json` (scenarios: `live`, `menu`, `stowed`, `novehicle`).

The world file at a glance (snippet A; run it a few times, and again in a big base):

```powershell
$f = "$env:LOCALAPPDATA\RRSOS-PCC-Live\live-world.json"
$w = Get-Content $f -Raw | ConvertFrom-Json
"{0}  inWorld={1}  pods={2} signs={3} containers={4} loose={5} extractors={6}  size={7:N0} KB" -f $w.updatedAt, $w.inWorld, $w.pods.Count, $w.signs.Count, $w.containers.Count, $w.loose.Count, $w.extractors.Count, ((Get-Item $f).Length / 1KB)
"scan: visited={0} frames={1} work={2} ms  worst chunk={3} ms" -f $w.scan.objectsVisited, $w.scan.frames, $w.scan.workMs, $w.scan.worstFrameMs
```

Which pods have a door, and what their sides are (snippet B; 4 = door, 2 = connection):

```powershell
$w.pods | Where-Object { $_.panels -contains 4 } | ForEach-Object { "{0}  {1}  panels={2}  at {3:N0},{4:N0}" -f $_.id, $_.group, ($_.panels -join ','), $_.position.x, $_.position.z }
```

Extractors as the plugin reports them (snippet C):

```powershell
$w.extractors | ForEach-Object { "{0,-6} {1,-16} {2,-12} {3}/{4}  ready={5}" -f $_.kind, $_.group, $_.productName, $_.count, $_.size, $_.ready }
```

Drones (snippet D; the ones in the air are in `live.json`, the stations in the world file):

```powershell
$live = Get-Content "$env:LOCALAPPDATA\RRSOS-PCC-Live\live.json" -Raw | ConvertFrom-Json
$live.drones.flying | ForEach-Object { "{0}  {1,-14} {2,-8}  at {3:N0},{4:N0}  speed={5}  cargo={6}" -f $_.id, $_.name, $_.state, $_.position.x, $_.position.z, $_.speed, (($_.cargo.items | ForEach-Object { "$($_.name) x$($_.count)" }) -join ', ') }
$w = Get-Content "$env:LOCALAPPDATA\RRSOS-PCC-Live\live-world.json" -Raw | ConvertFrom-Json
$w.droneStations | ForEach-Object { "{0}  {1}  at {2:N0},{3:N0}  docked={4}/{5}" -f $_.id, $_.group, $_.position.x, $_.position.z, $_.docked, $_.size }
```

Plugin problems in the world pass only:

```powershell
Select-String -Path "C:\Program Files (x86)\Steam\steamapps\common\The Planet Crafter\BepInEx\LogOutput.log" -Pattern 'RRSOS.*(world|placed object|container|loose item|game.s objects|planet.s hash|drone)'
```

Try the dashboard without the game, on fake data: `tools\sample-live.ps1 -Path .\sample\live.json -Loop` (it now writes `live-world.json` beside it too; there is a
new scenario, `nobases`). On a **real save**: `tools\save-to-world.ps1 -Save "$env:USERPROFILE\AppData\LocalLow\MijuGames\Planet Crafter\Custom-2.json" -OutFolder .\sample-save -Loop`,
then `dotnet run --project src\Dashboard -- --LiveFile=.\sample-save\live.json`. Both keep everything, including the base names, in that folder, never in the real one.
