# RRSOS PCC Live

A live, read-only view of **The Planet Crafter**: a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that reports
the game's state to a small file, and a dashboard that shows it on one page.

It is a separate project from [RRSOS-PCC](https://github.com/dkolln/RRSOS-PCC), the companion dashboard. That app works
only from the game's **save file**, which the game writes only when it saves, so nothing in it can be truly live. This
project reads the running game instead. Neither depends on the other to build.

```
Planet Crafter  --(plugin, once a second)-->  live.json        --(watches)-->  Dashboard (browser)
                --(plugin, every 5 seconds)-> live-world.json  --(watches)-->
                                              %LOCALAPPDATA%\RRSOS-PCC-Live
```

## What the dashboard shows

One page, laid out for a 2560 x 1440 monitor, three columns, nothing scrolls except the lists inside cards:

| Left | Middle | Right |
|---|---|---|
| **Player**: compass, altimeter, and two small maps: the **bases around you** (hover for a name, click to pick one) and the **drones around you** (stations, and drones in the air moving live; click it to open a drone card under the power card), oxygen, health, thirst, toxicity, backpack and worn gear | **Planet**: six terraformation dials with live rates, rocket count and multiplier under each, power dial and a picture per generator; **click the drone map** and a drone card opens under it | **Base**: what the base you are at (or picked on the map) holds: stored items, crops ready to harvest, loose items, each showing 10 rows and then scrolling |
| **Vehicle**: compass, map with you in the middle and the truck placed relative to you, altimeter, trunk contents, equipped modules | | **Extractors**: every ore, gas, water and algae machine, grouped (ore by what it mines), with its fill level, position, and distance and direction from you |

The backpack, gear and trunk lists can be folded by clicking their titles.

The only control is **LAUNCH PC**, which starts the game through Steam. Nothing on the page can change the game.

## Which starts first, the game or the app?

Either. The dashboard reads the plugin's file, and with no fresh file it just says `WAITING FOR THE GAME`. It turns
`LIVE` as soon as the game is in a world, whether you opened the page before or after. If you close the game the
page keeps the last reading, dimmed, and says so. `LAUNCH PC` is disabled while the game is running.

## Principles

- **Read-only.** The plugin observes the game. It never changes game state, saves, items or settings.
- **Awareness, not shortcuts.** It shows what the game already knows; it does not help anyone bypass how the game is played.
- **One small contract.** Everything leaves the game through two versioned JSON files, the fast one and the slow one ([docs/contract.md](docs/contract.md)).
- **Nothing of the game is redistributed.** The project references the game's assemblies in place. Game files, and the
  game's decompiled source, are never copied into this repo.
- **Nothing is trusted blindly.** Every section of the file is read on its own; if one fails it becomes `null` and the
  rest still arrive, and the dashboard copes with any of them missing.

## Setup

1. Install the .NET SDK (10 works) and the game (Steam).
2. Install BepInEx 5.4.23.4 into the game folder: see [docs/install-log.md](docs/install-log.md) for exactly what that
   adds and how to undo it. Playing with a plugin installed marks saves `"modded": true` (cosmetic, explained in
   [docs/game-notes.md](docs/game-notes.md)); back your saves up first.
3. If the game is not in `C:\Program Files (x86)\Steam\steamapps\common\The Planet Crafter\`, copy
   `solution_private.targets.example` to `solution_private.targets` and set `GameDir`.
4. Build the plugin (game closed): `dotnet build src/Live/Live.csproj`. It copies itself into
   `BepInEx\plugins\RRSOS-PCC-Live\`.
5. Run the dashboard: press start on the `Dashboard` profile in Visual Studio, or `dotnet run --project src/Dashboard`. It opens a console window and your default browser at http://localhost:5320 (full screen, F11, on a 2K monitor).

To try the dashboard **without the game**, generate fake live files:
`tools\sample-live.ps1 -Path .\sample\live.json -Loop`, then
`dotnet run --project src/Dashboard --LiveFile=.\sample\live.json` (the world file is written beside it, and the base names are kept there too).
To see the bases and extractors of a **real save**, `tools\save-to-world.ps1` turns one into the same two files (it only reads the save).

## Testing

**Read [docs/handoff.md](docs/handoff.md)** for where things stand and what is next, and **[docs/test-plan.md](docs/test-plan.md)** for what to check in the game. The plugin was written without being able to run the
game; the plan lists what to check in the game, what should happen, and which assumptions are the likely places for surprises.

## Modules

Built one at a time.

| # | Module | State |
|---|---|---|
| 0 | **Skeleton**: repo, solution, plugin that builds against the game's assemblies | done |
| 1 | **Install and verify**: BepInEx in the game, plugin loads | done: loads on Unity 6000.3.2 |
| 2 | **Discovery**: decompile the game's code and record the API in `docs/game-notes.md` | done for everything used so far |
| 3 | **Player**: position, yaw, vitals | done and verified in the game (position matches a save exactly) |
| 4 | **Inventories and vehicle**: backpack, gear, vehicle position, trunk, gear | done: run in the game, all sections arrive (vehicle position matches the save); detailed checks still open in the test plan |
| 5 | **Planet**: world-unit values and live rates, power and generators, rockets | done: run in the game; rocket multipliers match the wiki maths exactly, power and generators plausible; screen comparisons still open |
| 6 | **Dashboard**: one page for Player, Planet, Vehicle; Launch PC | done: fake data in every state at 2560 x 1440, then live with the real game (the owner: "beautiful") |
| 7 | **Base and Extractors**: base data, a map of nearby bases on the Player card, a Base card and an Extractors card on the one page | built (plugin 0.3.0, second file `live-world.json`); checked on fake data and a real save, **not yet run in the game**: see section 9 of docs/test-plan.md |
| 8 | **Drones**: a live drone map on the Player card, and a drone card (flying drones, stations, their contents) under the power card | built (plugin 0.3.0: `drones` in `live.json`, `droneStations` in `live-world.json`); checked on fake data only, **not yet run in the game**: see section 10 of docs/test-plan.md |
| 9 | **Optional**: local HTTP feed, in-game overlay | later |

## License

Not chosen yet. The modding community recommends a permissive one such as Apache 2.0.
