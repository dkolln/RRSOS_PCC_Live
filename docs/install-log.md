# Install log

What was put into the game folder, so it can be undone exactly.

## 2026-09-21: BepInEx 5.4.23.4 installed (module 1)

- Downloaded `BepInEx_win_x64_5.4.23.4.zip` (638,940 bytes) from the official release page,
  https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.4. Its SHA-256 matched the digest GitHub publishes:
  `f881201b79da03e513bf97cdf39607ffa7f9e0d31a519b1aeeca8eb60f8309e7`.
- Extracted into `C:\Program Files (x86)\Steam\steamapps\common\The Planet Crafter\`.
- The game folder's top level held exactly `D3D12`, `MonoBleedingEdge`, `Planet Crafter_Data`, `Planet Crafter.exe`,
  `UnityCrashHandler64.exe`, `UnityPlayer.dll` before. Nothing there was changed or removed.
- **Added:** `BepInEx\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`.
  The plugin is deployed by a build to `BepInEx\plugins\RRSOS-PCC-Live\RRSOS.PCC.Live.dll`.
- Saves were backed up first (both `Custom-1.json` and `Custom-2.json`, byte-for-byte verified) to
  `%USERPROFILE%\Documents\PlanetCrafter-SaveBackups\2026-09-21_1317\`.

## Undo

Either switch it off without deleting anything: set `enabled = false` in `doorstop_config.ini`.

Or remove it completely: delete `BepInEx\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and
`changelog.txt` from the game folder. The game is then as it was. (Steam's "Verify integrity of game files" would not
remove them, since they are not part of the game, so delete them by hand.)

## First run

- Launch the game once and quit. BepInEx creates `BepInEx\LogOutput.log`, `config\` and `cache\`.
- The plugin's line should appear in that log: `RRSOS PCC Live 0.0.1 loaded.`
- Then check the two open questions in `game-notes.md`: does BepInEx 5 work on Unity 6000.3, and does a modded
  session flag a save as modded?
