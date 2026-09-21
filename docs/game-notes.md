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
- The save has a `modded` flag. Whether running a plugin changes it is **not yet known** (module 1 finds out).

## Open questions

- Does BepInEx 5.4.23.4 work on Unity 6000.3? (The wiki was written for older Unity versions.)
- Does a modded session flag saves as modded, and does that matter for anything?
- Which classes hold the player's position, heading and vitals? (module 2)
