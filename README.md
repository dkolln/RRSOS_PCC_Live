# RRSOS PCC Live

A live, read-only view of **The Planet Crafter**: a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that reports
the game's state to a small file, and a dashboard that shows it on one page.

It is a separate project from [RRSOS-PCC](https://github.com/dkolln/RRSOS-PCC), the companion dashboard. That app works
only from the game's **save file**, which the game writes only when it saves, so nothing in it can be truly live. This
project reads the running game instead. Neither depends on the other to build.

```
Planet Crafter  --(plugin, once a second)-->  live.json  --(watches)-->  Dashboard (browser)
                                              %LOCALAPPDATA%\RRSOS-PCC-Live
```

## What the dashboard shows

One page, laid out for a 2560 x 1440 monitor, three cards:

| Player | Planet | Vehicle |
|---|---|---|
| compass, position map, altimeter | six terraformation dials with live rates | compass, map with you in the middle and the truck placed relative to you, altimeter |
| oxygen, health, thirst, toxicity | rocket count and multiplier under each dial | trunk contents |
| backpack contents | power dial (produced, used, left) and a picture per generator | equipped modules |
| worn gear | | |

The only control is **LAUNCH PC**, which starts the game through Steam. Nothing on the page can change the game.
Base and Extractors will be added later.

## Which starts first, the game or the app?

Either. The dashboard reads the plugin's file, and with no fresh file it just says `WAITING FOR THE GAME`. It turns
`LIVE` as soon as the game is in a world, whether you opened the page before or after. If you close the game the
page keeps the last reading, dimmed, and says so. `LAUNCH PC` is disabled while the game is running.

## Principles

- **Read-only.** The plugin observes the game. It never changes game state, saves, items or settings.
- **Awareness, not shortcuts.** It shows what the game already knows; it does not help anyone bypass how the game is played.
- **One small contract.** Everything leaves the game through one versioned JSON file ([docs/contract.md](docs/contract.md)).
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
5. Run the dashboard: `dotnet run --project src/Dashboard`, then open http://localhost:5320 (full screen, F11, on a 2K monitor).

To try the dashboard **without the game**, generate a fake live file:
`tools\sample-live.ps1 -Path .\sample\live.json -Loop`, then
`dotnet run --project src/Dashboard --LiveFile=.\sample\live.json`.

## Testing

**Read [docs/test-plan.md](docs/test-plan.md).** Everything in the plugin was written without being able to run the
game; the plan lists what to check in the game, what should happen, and which assumptions are the likely places for surprises.

## Modules

Built one at a time.

| # | Module | State |
|---|---|---|
| 0 | **Skeleton**: repo, solution, plugin that builds against the game's assemblies | done |
| 1 | **Install and verify**: BepInEx in the game, plugin loads | done: loads on Unity 6000.3.2 |
| 2 | **Discovery**: decompile the game's code and record the API in `docs/game-notes.md` | done for everything used so far |
| 3 | **Player**: position, yaw, vitals | done and verified in the game (position matches a save exactly) |
| 4 | **Inventories and vehicle**: backpack, gear, vehicle position, trunk, gear | written; builds; **not yet run in the game** |
| 5 | **Planet**: world-unit values and live rates, power and generators, rockets | written; builds; **not yet run in the game** |
| 6 | **Dashboard**: one page for Player, Planet, Vehicle; Launch PC | written; **tested against fake data only** (all states, 2560 x 1440) |
| 7 | **Base and Extractors** | later |
| 8 | **Optional**: drones, local HTTP feed, in-game overlay | later |

## License

Not chosen yet. The modding community recommends a permissive one such as Apache 2.0.
