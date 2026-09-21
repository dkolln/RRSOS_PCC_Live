# RRSOS PCC Live

A read-only [BepInEx](https://github.com/BepInEx/BepInEx) plugin for **The Planet Crafter** that reports live game
state (player position, vitals, vehicle, drones, rockets, and more) to a small file that companion apps can read.

It is a separate project from [RRSOS-PCC](https://github.com/dkolln/RRSOS-PCC), the companion dashboard. That app works only from the
game's **save file**, which the game writes only when it saves, so nothing in it can be truly live. This plugin is the
live source. The dashboard can read from it later; neither project depends on the other to build.

## Principles

- **Read-only.** The plugin observes the game. It never changes game state, saves, items or settings.
- **Awareness, not shortcuts.** It exists to show what the game already knows, not to bypass how the game is played.
- **One small contract.** Everything leaves the game through one versioned JSON file ([docs/contract.md](docs/contract.md)),
  so the plugin and the apps that read it can change independently.
- **Nothing of the game is redistributed.** The project references the game's assemblies in place. Game files are
  never copied into this repo.

## What is known about the game

See [docs/game-notes.md](docs/game-notes.md): Unity 6000.3.2 on Mono, BepInEx 5.x, the game's class names found so far.

## Setup

1. Install the .NET SDK (10 works) and the game (Steam).
2. If the game is not in `C:\Program Files (x86)\Steam\steamapps\common\The Planet Crafter\`, copy
   `solution_private.targets.example` to `solution_private.targets` and set `GameDir`.
3. `dotnet build src/Live/Live.csproj`. Once BepInEx is installed in the game, a build also copies the plugin into
   `BepInEx\plugins\RRSOS-PCC-Live\`.

## Modules

Built one at a time; each one is small enough to test in the game before the next starts.

| # | Module | State |
|---|---|---|
| 0 | **Skeleton**: repo, solution, plugin that builds against the game's assemblies and logs a line | done |
| 1 | **Install and verify**: put BepInEx into the game, see the plugin's log line, check whether saves get flagged as modded | done: loads on Unity 6000.3.2; saves are flagged `modded` while `BepInEx\plugins` is non-empty (cosmetic, see game-notes) |
| 2 | **Discovery**: decompile the game's assembly and record the types and members we need in `docs/game-notes.md` | done for player, vitals, planet rates, power, world objects, drones; the vehicle is left for module 4 |
| 3 | **Player**: position, heading, vitals written to the live file about once a second | done: verified in the game (position and yaw match a save exactly; vitals match apart from normal drain) |
| 4 | **Vehicle and drones**: where they are right now | |
| 5 | **World**: planet levels, launched rockets, power | |
| 6 | **Contract v1**: freeze the file format and add a reader to RRSOS-PCC (in that repo) | |
| 7 | **Optional**: local HTTP or socket feed, in-game overlay | |

## License

Not chosen yet. The modding community recommends a permissive one such as Apache 2.0.
