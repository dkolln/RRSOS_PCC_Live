<#
  Sets this project up for the PC it is on: finds The Planet Crafter through Steam, writes solution_private.targets
  (which the build reads for the game folder), and reports what else the plugin and the dashboard will use here.
  Safe to run again at any time. It only reads Steam's and the game's files; the one file it writes is
  solution_private.targets at the root of this repo (git ignores it).

    .\tools\setup.ps1                 # find the game and write solution_private.targets
    .\tools\setup.ps1 -GameDir "D:\Games\The Planet Crafter"   # or say where it is
    .\tools\setup.ps1 -WhatIf         # only report; write nothing
#>
param(
  [string] $GameDir,
  [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'
$AppId = '1284190'          # The Planet Crafter on Steam
$Root = Split-Path -Parent $PSScriptRoot

function Test-GameDir($dir) { $dir -and (Test-Path (Join-Path $dir 'Planet Crafter_Data\Managed\Assembly-CSharp.dll')) }

# Steam's own list of its library folders (each holds steamapps\appmanifest_<id>.acf for the games installed there).
function Find-SteamGame {
  $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
  if (-not $steam) { $steam = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -Name InstallPath -ErrorAction SilentlyContinue).InstallPath }
  if (-not $steam) { Write-Host "Steam is not installed for this user (no SteamPath in the registry)."; return $null }

  $steam = $steam -replace '/', '\'
  $libraries = @($steam)
  $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
  if (Test-Path $vdf) {
    foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libraries += $m.Groups[1].Value -replace '\\\\', '\' }
  }

  foreach ($library in ($libraries | Select-Object -Unique)) {
    $manifest = Join-Path $library "steamapps\appmanifest_$AppId.acf"
    if (-not (Test-Path $manifest)) { continue }

    $installDir = [regex]::Match((Get-Content $manifest -Raw), '"installdir"\s+"([^"]+)"').Groups[1].Value
    $dir = Join-Path $library "steamapps\common\$installDir"
    if (Test-GameDir $dir) { return $dir }
  }

  Write-Host "Steam libraries checked: $($libraries -join '; ')"
  return $null
}

if ($GameDir) {
  if (-not (Test-GameDir $GameDir)) { throw "No Planet Crafter in '$GameDir' (expected Planet Crafter_Data\Managed\Assembly-CSharp.dll under it)." }
} else {
  $GameDir = Find-SteamGame
  if (-not $GameDir) { throw "Could not find The Planet Crafter through Steam. Run again with -GameDir `"<the game's folder>`"." }
}

$GameDir = (Resolve-Path $GameDir).Path.TrimEnd('\') + '\'
$bepinex = Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.dll')
$plugin = Join-Path $GameDir 'BepInEx\plugins\RRSOS-PCC-Live\RRSOS.PCC.Live.dll'
$saves = Join-Path (Split-Path -Parent $env:LOCALAPPDATA) 'LocalLow\MijuGames\Planet Crafter'
$live = Join-Path $env:LOCALAPPDATA 'RRSOS-PCC-Live'

Write-Host ""
Write-Host "Game folder      $GameDir"
Write-Host ("BepInEx          " + $(if ($bepinex) { 'installed' } else { 'NOT installed: see docs\install-log.md before building the plugin' }))
Write-Host ("Plugin           " + $(if (Test-Path $plugin) { "installed ($plugin)" } else { 'not installed yet: build src\Live with the game closed' }))
Write-Host ("Saves            $saves" + $(if (Test-Path $saves) { '' } else { '  (not there yet: the game makes it on first save)' }))
Write-Host "Live files       $live  (the plugin makes it when the game starts)"
Write-Host ""

$targets = Join-Path $Root 'solution_private.targets'
$content = @"
<!-- Written by tools\setup.ps1 for this PC. Git ignores this file; run the script again if the game moves. -->
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <GameDir>$GameDir</GameDir>
  </PropertyGroup>
</Project>
"@

if ($WhatIf) {
  Write-Host "Would write $targets"
} else {
  [IO.File]::WriteAllText($targets, $content, (New-Object Text.UTF8Encoding($false)))
  Write-Host "Wrote $targets"
}

Write-Host "The dashboard needs nothing more on a normal Steam install. To point it elsewhere, see src\Dashboard\appsettings.json."
