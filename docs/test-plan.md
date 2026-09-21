# Test plan

> **Status (2026-09-21):** this plan was written before the game could be run. The first real run has since happened:
> every section arrived with no plugin warnings, the rocket multipliers matched the wiki maths, the vehicle position
> matched a save, and the dashboard looked right against real data. Items below are still worth ticking off
> individually (many need a specific action in the game); untested ones are the open questions.
>
> What was tested without the game: the plugin compiles against the game's real assemblies, and the dashboard was
> exercised against a fake file (`tools/sample-live.ps1`) in every state at 2560 x 1440.

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

- [ ] Line `RRSOS PCC Live 0.2.0 loaded. Read-only ...` appears.
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

- [ ] **Position:** the coordinates under the map and the altimeter number match what you expect; walk and both change smoothly.
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
  full backpack. (Browser full screen, F11, is the intended way to run it.) The lower half of each card is deliberately empty for the future Base and Extractors.
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
9. **Drones, bases and extractors** are not in the file yet.

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
