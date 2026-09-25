# Installing RRSOS PCC Live on a PC

This gets the plugin into The Planet Crafter and the dashboard running in your browser. It takes about 15 minutes.
Nothing here needs to know where things live on your PC: the setup script finds the game through Steam.

There are two parts:

- **The plugin** runs inside the game through **BepInEx**, a mod loader. You install BepInEx into the game folder once,
  and then the build copies the plugin in beside it.
- **The dashboard** is a small local web app. It reads the files the plugin writes and shows them at
  http://localhost:5320.

## What you need

| | |
|---|---|
| Windows 10 or 11 | |
| **The Planet Crafter**, the Steam version | The Xbox / Game Pass version is not supported: BepInEx is not set up for it here. |
| **.NET 10 SDK** | https://dotnet.microsoft.com/download/dotnet/10.0, or `winget install Microsoft.DotNet.SDK.10`. Check with `dotnet --version` (10.x). |
| **Git** | To get the code: https://git-scm.com, or `winget install Git.Git`. |
| **BepInEx 5.4.23.4, x64** | Step 3 below. Use this exact one: BepInEx **5**, the **win x64** build. (The game runs on Mono, which is what BepInEx 5 is for; BepInEx 6 and the IL2CPP builds are not.) |

## 1. Back up your saves

Playing with any BepInEx plugin makes the game mark a save `"modded": true`. That is cosmetic (see
[docs/game-notes.md](docs/game-notes.md)), but back up first anyway. The saves are in:

```
%USERPROFILE%\AppData\LocalLow\MijuGames\Planet Crafter
```

Copy the `.json` files in that folder somewhere safe.

## 2. Get the code

```powershell
git clone https://github.com/dkolln/RRSOS_PCC_Live.git
cd RRSOS_PCC_Live
```

## 3. Install BepInEx into the game

1. Download **`BepInEx_win_x64_5.4.23.4.zip`** from the official release page:
   https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.4
2. Optional: check the download is the real one. In PowerShell, `Get-FileHash .\BepInEx_win_x64_5.4.23.4.zip` should
   print `F881201B79DA03E513BF97CDF39607FFA7F9E0D31A519B1AEECA8EB60F8309E7`.
3. Find the game folder: in Steam, right-click **The Planet Crafter** > **Manage** > **Browse local files**. It is the
   folder with `Planet Crafter.exe` in it.
4. Extract the zip **into that folder**, so that `winhttp.dll`, `doorstop_config.ini` and a `BepInEx` folder sit right
   next to `Planet Crafter.exe`. (Not inside a subfolder of their own.)
5. Start the game once, wait for the main menu, and quit. BepInEx makes its folders on that first run: check that
   `BepInEx\LogOutput.log` now exists in the game folder.

[docs/install-log.md](docs/install-log.md) lists exactly what BepInEx adds and how to take it out again.

## 4. Point the project at your game

```powershell
powershell -ExecutionPolicy Bypass -File tools\setup.ps1
```

It finds the game in any of your Steam libraries (on any drive) and writes `solution_private.targets`, which the build
reads. It also reports whether BepInEx is installed, and where your saves and the plugin's files are. It only reads Steam's
and the game's files, and you can run it again at any time. If it cannot find the game, tell it where it is:

```powershell
powershell -ExecutionPolicy Bypass -File tools\setup.ps1 -GameDir "D:\SteamLibrary\steamapps\common\The Planet Crafter"
```

## 5. Build the plugin (game closed)

```powershell
dotnet build src\Live\Live.csproj
```

The last lines should include `Deployed RRSOS.PCC.Live.dll to ...\BepInEx\plugins\RRSOS-PCC-Live\`. **The game must be
closed**, or Windows will not let the build replace the plugin.

## 6. Start the game and check the plugin loaded

Start The Planet Crafter as usual, then open `BepInEx\LogOutput.log` in the game folder. You should see a line like:

```
[Info   :RRSOS PCC Live] RRSOS PCC Live 0.4.1 loaded. Read-only: it reports game state and never changes it. ...
```

Once you load a world, the plugin writes its files to `%LOCALAPPDATA%\RRSOS-PCC-Live`.

## 7. Run the dashboard

```powershell
dotnet run --project src\Dashboard
```

It opens your browser at http://localhost:5320. (In Visual Studio, open `RRSOS.PCC.Live.slnx` and start the
**Dashboard** profile instead.) It says `WAITING FOR THE GAME` until the game is in a world, then `LIVE`. The game and
the dashboard can be started in either order. The page is laid out for a 2560 x 1440 screen; F11 for full screen.

## Installing from a release (no building)

A release zip has the plugin already built and the dashboard already published, so there is nothing to compile.

1. **Back up your saves** (step 1 above) and **install BepInEx 5.4.23.4 into the game** (step 3 above). You still need both.
2. Install the **.NET 10 ASP.NET Core Runtime** (Windows x64) if you do not have it: https://dotnet.microsoft.com/download/dotnet/10.0
   (`dotnet --list-runtimes` should list `Microsoft.AspNetCore.App 10.0.x`).
3. Unzip the release anywhere.
4. Copy `plugin\RRSOS.PCC.Live.dll` into `BepInEx\plugins` in the game folder. **Close the game first**; after a plugin
   update the game needs a **full restart**, not just going back to the main menu.
5. Start the game, then run the dashboard: double-click `dashboard\RRSOS.PCC.Dashboard.exe` (or
   `dotnet dashboard\RRSOS.PCC.Dashboard.dll`) and open http://localhost:5320. Settings for one PC go in
   `dashboard\appsettings.Local.json` (see "Settings for this PC" below).

To update: close the game, copy the new plugin DLL over the old one, replace the `dashboard` folder (keep your
`appsettings.Local.json`), and restart both. Your notes, base names and Cheats configs live in
`%LOCALAPPDATA%\RRSOS-PCC-Live` and are not touched.
## Updating

```powershell
git pull
```

Then **close the game**, rebuild the plugin (step 5), and restart the dashboard. After a plugin update the game needs a
**full restart**: going back to the main menu and loading a world again does not load new plugin code.

## Settings for this PC

On a normal Steam install the dashboard needs no settings. To change something for one PC only, create
`src\Dashboard\appsettings.Local.json` (git ignores it) with just the settings you want. The full list, with what each
one defaults to, is in [src/Dashboard/appsettings.json](src/Dashboard/appsettings.json). For example:

```json
{
  "Urls": "http://localhost:5400",
  "SaveSettings": { "SavePath": "D:\\PlanetCrafterSaves" }
}
```

| Setting | What it changes | Default |
|---|---|---|
| `Urls` | The dashboard's address and port | `http://localhost:5320` |
| `LiveFolder` | Where the plugin's files are, and where the dashboard keeps base names, notes, resupply configs and save backups | `%LOCALAPPDATA%\RRSOS-PCC-Live` |
| `SaveSettings:SavePath` | The save folder the Cheats page edits | `%USERPROFILE%\AppData\LocalLow\MijuGames\Planet Crafter` |
| `Game:LaunchUri` | What **LAUNCH PC** opens | `steam://rungameid/1284190` |
| `Game:ProcessName` | The process that means the game is running | `Planet Crafter` |

Any of them can also be given on the command line, which wins over the file:
`dotnet run --project src\Dashboard -- --Urls=http://localhost:5400`.

## When something is wrong

| What you see | What to do |
|---|---|
| Build error: *The Planet Crafter was not found at ...* | Run `tools\setup.ps1` (step 4), or give it `-GameDir`. |
| Build error that it cannot copy `RRSOS.PCC.Live.dll` | The game is running. Close it and build again. |
| `setup.ps1` is blocked (*running scripts is disabled*) | Run it as shown, through `powershell -ExecutionPolicy Bypass -File ...`. |
| No `LogOutput.log` in the game folder | BepInEx is not installed in the right place: `winhttp.dll` must be next to `Planet Crafter.exe`. |
| The log has no `RRSOS PCC Live ... loaded` line | The plugin is not in `BepInEx\plugins\RRSOS-PCC-Live\`. Build it again with the game closed, then restart the game. |
| The dashboard stays on `WAITING FOR THE GAME` | Load a world (the plugin only writes while you are in one), and check the log line above. |
| The dashboard will not start, or the page shows an error and drops you | Another dashboard is already running on the same port. Close it, or give this one another `Urls`. |
| Changes to the plugin do not show up | Restart the game fully: the plugin is only loaded when the game starts. |
| You want a different voice for the spoken alerts | They use Windows' default text-to-speech voice: **Settings > Time & language > Speech**, then restart the dashboard. They play on the PC running the dashboard. (Narrator's own voice setting is separate and not used.) |
| The Boneyard is empty or out of date | It comes from the last save, not live: save the game (or wait for an autosave) and it updates within 10 seconds. Its title shows the save's time. |

## Uninstalling

- The dashboard: delete the folder you cloned, and `%LOCALAPPDATA%\RRSOS-PCC-Live` (the plugin's files and the
  dashboard's base names, notes and save backups).
- The plugin: delete `BepInEx\plugins\RRSOS-PCC-Live\` from the game folder.
- BepInEx: see [docs/install-log.md](docs/install-log.md), "Undo". To just switch it off, set `enabled = false` in
  `doorstop_config.ini`.
