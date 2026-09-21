# Game notes

Facts about The Planet Crafter that this project depends on. Each line says where it came from, so it can be
re-checked after a game update.

## The install (checked 2026-09-21)

| Item | Value | Source |
|---|---|---|
| Install folder | `C:\Program Files (x86)\Steam\steamapps\common\The Planet Crafter\` | Steam manifest `appmanifest_1284190.acf` (an empty leftover folder also exists on `F:\`) |
| Steam build id | 25296421 | same manifest |
| Unity | 6000.3.2f1 | `UnityPlayer.dll` file version |
| Scripting backend | **Mono** (`Planet Crafter_Data\Managed\Assembly-CSharp.dll` exists, no IL2CPP data) | file check |
| Game code | `Assembly-CSharp.dll` (about 1.4 MB) and `Assembly-CSharp-firstpass.dll` | file check |
| BepInEx | not installed yet | folder check |
| Save version | `2.103` (field `version` in the save) | a save file |

A game update changes the build id. If a module breaks after an update, start by re-checking this table.

## Modding setup (from the community wiki, "Developing C# Mods")

- Framework: **BepInEx 5.4.23.4**, Windows x64, unzipped next to `Planet Crafter.exe`. Run the game once so it creates its folders.
- Plugins target `netstandard2.1`. Game code is patched with Harmony (`[HarmonyPostfix]`, `[HarmonyPatch]`, `__instance`).
- Small getters and setters can be inlined by the runtime, so a Harmony patch on them may silently do nothing.
- Namespace of the main behaviours: `SpaceCraft` (for example `SpaceCraft.Intro`).

## Hooks named by the wiki (unverified against this game version)

| Purpose | Type and member |
|---|---|
| Main menu start | `Intro.Start` |
| Entering a world | `PlanetLoader.HandleDataAfterLoad`, then wait for `PlanetLoader.GetIsLoaded()` to be true |
| Leaving a world | `UiWindowPause.OnQuit` |
| Finding objects | `Managers.GetManager<T>()` (null until a world is loaded), `Object.FindObjectsByType<T>`, `GameObject.Find` |
| World objects and item definitions | `WorldObjectsHandler.Instance`, `GroupsHandler.GetGroupViaId` |

## The save file (what the dashboard already understands)

Sections separated by `@`, records by `|`. Facts that matter to a live feed:

- The player, the vehicle and world objects carry `pos` (x,y,z) and `rot` (quaternion).
- Launched rockets sit in hidden `SpaceMultiplier*` containers at position -500,-500,-500.
- Drones docked in a station have no position; airborne ones do.
- A world object with no `pos` is inside an inventory, or "away" (pocket, portal, space).
- The save has a `modded` flag (see "The modded flag" below for what sets it).

## Module 1 results (2026-09-21)

- **BepInEx 5.4.23.4 works on Unity 6000.3.2.** The log reads "Running under Unity v6000.3.2.11106207", the chainloader
  started, and our plugin loaded and logged its line.
- The game itself shows a red "Mods Detected - mods are not officially supported..." notice on the main menu. That is
  expected and cosmetic.
- BepInEx logs `[Error] Unable to start Unity log writer` at startup. It only means Unity's own log is not mirrored
  into `LogOutput.log`; plugins are unaffected. Ignore unless a later module needs Unity's log.
- Saves untouched by simply loading a world and quitting without saving.

## The "modded" flag (answered 2026-09-21)

A save written with the plugin installed got `"modded": true` (checked on a real save). The game decides this in
`ModHelper.GetIsModded()`: it is true when the folder `BepInEx\plugins` exists next to the game and is **not empty**.
It has nothing to do with whether any plugin code runs, and `doorstop_config.ini` `enabled = false` does not change it.

What the flag does, as far as the decompiled code shows: it is written into the save (`JSONExport`), the HUD debug text
gets " - Modded", a few "hide if not modded" UI bits appear, and the in-game feedback form sends it along with bug
reports. It does not change gameplay. Emptying `BepInEx\plugins` makes the next save `false` again.

## Discovered API (module 2, build 25296421)

Found by decompiling `Assembly-CSharp.dll` with `ilspycmd`. Decompiled source is kept out of this repo (it is the
game's code); only names and signatures are recorded here. Everything is in `namespace SpaceCraft`.

**All the wiki's hook names exist:** `PlanetLoader` (`HandleDataAfterLoad`, `GetIsLoaded`), `UiWindowPause.OnQuit`,
`Intro`, `Managers.GetManager<T>()`, `WorldObjectsHandler`, `GroupsHandler`.

| Want | Where | Notes |
|---|---|---|
| The player | `Managers.GetManager<PlayersManager>().GetActivePlayerController()` returns `PlayerMainController` | A `MonoBehaviour`, so `.transform.position` and `.rotation` are live. `PlayersManager.playersControllers` lists all players (multiplayer). `RegisterToLocalPlayerStarted(Action)` fires when the local player exists |
| Vitals | `PlayerMainController.GetPlayerGaugesHandler()` | `GetPlayerOxygenValue()`, `GetPlayerThirstValue()`, `GetPlayerHealthValue()`, `GetPlayerToxicValue()`, `GetPlayerIsDying()` |
| Planet stats and their live rates | `Managers.GetManager<WorldUnitsHandler>().GetUnit(DataConfig.WorldUnitType.X)` returns `WorldUnit` | `GetValue()`, `GetIncreaseValuePersSec()`, `GetDecreaseValuePersSec()`, `GetCurrentValuePersSec()`, `IsIncreasing()`. Types: Oxygen, Energy, Heat, Pressure, Terraformation, Biomass, Plants, Insects, Animals, SystemTerraformation, Purification |
| Power | the `Energy` unit (`WorldUnitEnergy`, label `kW`) | To confirm in module 5: how increase and decrease map to production and use |
| Any placed thing | `WorldObjectsHandler.Instance`, then `WorldObject` | `GetId()`, `GetGroup()`, `GetPosition()`, `GetRotation()`, `GetGameObject()` (the live scene object), `GetLinkedInventoryId()`, `GetPlanetHash()`, `GetEnergy()`, `GetUnitGeneration(type)`, `GetUnitMultiplier(type)` |
| Rocket multipliers | `WorldUnitMultiplierViaInventory` and `WorldUnitGenerationViaInventory` (components tied to an inventory) | The game applies rocket bonuses itself; the live rates already include them |
| Drones | `Drone` is a `MonoBehaviour` (find with `Object.FindObjectsByType<Drone>`) | Live `transform.position`; `GetDroneInventory()`, `GetLogisticTask()`, `GetDronePlanetHash()`. `MachineDroneStation` is the station |
| The vehicle | `VehicleController`, a `MonoBehaviour` from the third-party physics asset in `EVP5.dll` (Edy's Vehicle Physics; the game's `ActionTakeControl` uses it to enter a vehicle) | Should give a live `transform.position` and speed for the vehicle in the scene, found with `Object.FindObjectsByType`. Needs a reference to `EVP5.dll`. Related game classes: `VehicleShareData`, `VehicleEquipment`, `VehicleJetpack`, `VehicleBackToGarage` (the recall). **Unverified:** the exact namespace, and whether the object exists while the vehicle is stowed (module 4) |

**Why this matters for the dashboard:** the save file cannot hold power, TI rates or live positions, so RRSOS-PCC has
had to rebuild them from tables and multipliers. The game already knows all of them, including the effect of every
machine, optimizer and rocket. Reading `WorldUnit` rates would replace most of that reconstruction.

## Discovered API (module 7: bases and extractors, from the decompiled code, build 25296421)

Read from the decompile only; **none of this has been run in the game yet** (see the test plan, section 9).

| Want | Where | Notes |
|---|---|---|
| Every object | `WorldObjectsHandler.Instance.GetAllWorldObjects()` (`Dictionary<int, WorldObject>`) | Copy the values before walking them across frames |
| Placed things | `GetConstructedWorldObjects()` (`HashSet<WorldObject>`) | Holds every `GroupConstructible`, and buildable `GroupItem`s, whose id is not a scene id. **Includes ones not placed** (crafted, sitting in an inventory), so also check `GetIsPlaced()` |
| Is it in the world | `WorldObject.GetIsPlaced()` | True when the position is not (0,0,0). An object put in an inventory has its position reset, and the save then has no `pos` |
| Scene objects | `WorldObjectsIdHandler.IsWorldObjectFromScene(id)` | True for ids below 200,000,000: the landscape's own objects (rocks, wreck loot and containers). Things the player builds or drops get ids of 200,000,000 or more |
| Which planet | `WorldObject.GetPlanetHash()` against `PlanetLoader.GetCurrentPlanetData().GetPlanetHash()` | The game itself compares these two to decide what to show. `GetPlanetHash()` is 0 for an object that is not placed |
| Pod panels | `WorldObject.GetPanelsId()` (`List<int>`), values of `DataConfig.BuildPanelSubType` | 0 WallNull, 1 WallPlain, **2 WallCorridor** (a connection), 3 WallGlass, **4 WallDoor** (the entrance), 5 FloorLight, 6 FloorGlass, 7 FloorPlain, 8 FloorLab, 9 WallLab, 10 FloorNoLight, 11 WallInside, 12 None, 13 WallWaterLife, 14 to 16 the angled floors. The same numbers are in the save's `pnls`, and match what RRSOS-PCC's `PanelParser` uses |
| Pod group ids | seen in a save: `pod`, `Pod4x`, `Pod9xC`, `EscapePod` | The crash pod, `EscapePod`, has no panels in the save. RRSOS-PCC only treats `pod` (and, by a case-sensitive match that misses `EscapePod`, `Escapepod`) as candidates for bases |
| Sign text | `WorldObject.GetText()` on a group whose id is `Sign` | |
| Storage | `HasLinkedInventory()` and `GetLinkedInventoryId()`, `GetSecondaryInventoriesId()` (a list), then `InventoriesHandler.Instance.GetInventoryById(id)`; `Inventory.GetSize()` and `GetInsideWorldObjects()` | A grower keeps its plants in the *secondary* inventory. Ore and gas extractors and water collectors use the linked one, and algae generators the secondary one (as in the save's `liId` and `siIds`) |
| What an extractor produces | `WorldObject.GetLinkedGroups()` (`List<Group>`) | The save's `liGrps`. The first group is the product |
| A plant's growth | `WorldObject.GetGrowth()` (0 to 100) | 100 is fully grown, as the save's `grwth` |
| Extractor group ids | seen in a save: `OreExtractor2`, `OreExtractor3`, `GasExtractor2`, `WaterCollector1`, `WaterCollector2`, `AlgaeGenerator1`, `AlgaeGenerator2`, `GeneticExtractor1` | Matched by prefix. The plugin reads ore, gas, water and algae; the genetic extractor and the water-life collector are left out |
| Loose items | a placed `GroupItem` that is not from the scene | What the dashboard calls the "boneyard" |

Plugin cost: the world pass walks the game's placed objects (thousands) and then every object (tens of thousands), about every
5 seconds, on the main thread. It stops after roughly 2 ms per frame and carries on next frame; the pass reports its own
`scan.workMs` and `scan.worstFrameMs` in the file so the cost can be read off after a real session.

## Open questions

- How do `WorldUnitEnergy`'s increase and decrease map to power produced and power used? (module 5)
- Is the vehicle object present in the scene while stowed in the pocket, or only when deployed? (module 4)
- Is the `Drone` component only present while a drone is airborne? (module 4)
- Multiplayer: `playersControllers` may hold several players. Use `GetActivePlayerController()` (the local one).
