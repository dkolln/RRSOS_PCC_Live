<#
  Turns a real SAVE FILE into a live.json + live-world.json pair, so the dashboard's bases and extractors can be checked
  against real data without starting the game (and compared with what RRSOS-PCC shows for the same save).
  Read-only on the save. The two files go to -OutFolder; run the dashboard on them with:

    dotnet run --project src/Dashboard -- --LiveFile=<OutFolder>\live.json

  What it can and cannot do, compared with the plugin:
    - pods, signs, containers near a base and extractors: the same facts the plugin reports (item names are the game's
      group ids, because the save holds no display names)
    - loose items on the ground: not produced here; the dashboard reads them from the newest save itself
    - live.json holds only the player's position and heading, so the cards for the planet and the vehicle stay empty

  .\tools\save-to-world.ps1 -Save "$env:USERPROFILE\AppData\LocalLow\MijuGames\Planet Crafter\Custom-2.json" -OutFolder .\sample-save
#>
param(
  [Parameter(Mandatory)] [string] $Save,
  [Parameter(Mandatory)] [string] $OutFolder,
  [switch] $Loop
)

$ci = [Globalization.CultureInfo]::InvariantCulture
$reach = 120.0

function Num($s) { [double]::Parse($s, $ci) }
function Pos($s) { $p = $s -split ','; @{ x = Num $p[0]; y = Num $p[1]; z = Num $p[2] } }

$text = [IO.File]::ReadAllText($Save).TrimStart([char]0xFEFF)
$records = @($text -split '[@|]' | Where-Object { $_.Trim() })
"{0} records" -f $records.Count

$objects = @{}      # id -> parsed record (anything with a gId)
$details = @{}      # inventory id -> @{ size; ids }
$player = $null

foreach ($r in $records) {
  if ($r.Contains('"woIds"')) {
    $d = $r | ConvertFrom-Json
    $details[[int64]$d.id] = @{ size = [int]$d.size; ids = @($d.woIds -split ',' | Where-Object { $_ } | ForEach-Object { [int64]$_ }) }
  } elseif ($r.Contains('"playerPosition"')) {
    $player = $r | ConvertFrom-Json
  } elseif ($r.Contains('"gId"')) {
    $o = $r | ConvertFrom-Json
    $objects[[int64]$o.id] = $o
  }
}
"{0} objects, {1} inventories" -f $objects.Count, $details.Count

function Tally($inventoryIds, [switch]$Growth) {
  $counts = @{}
  foreach ($iid in $inventoryIds) {
    if (-not $details.ContainsKey([int64]$iid)) { continue }
    foreach ($id in $details[[int64]$iid].ids) {
      $item = $objects[$id]
      if (-not $item) { continue }
      if (-not $counts.ContainsKey($item.gId)) { $counts[$item.gId] = @{ id = $item.gId; name = $item.gId; count = 0; ready = 0 } }
      $counts[$item.gId].count++
      if ($Growth -and $item.grwth -ge 100) { $counts[$item.gId].ready++ }
    }
  }
  @($counts.Values | ForEach-Object { $e = @{ id = $_.id; name = $_.name; count = $_.count }; if ($_.ready -gt 0) { $e.ready = $_.ready }; $e })
}

function Secondary($o) { if ($o.siIds) { @($o.siIds -split ',' | Where-Object { $_ }) } else { @() } }

# ---- pods and signs ----
$pods = @(); $podFlat = @()
foreach ($o in $objects.Values) {
  if ($o.gId -match '^(pod|EscapePod)' -and $o.pos) {
    $panels = @(); if ($o.pnls) { $panels = @($o.pnls -split ',' | ForEach-Object { [int]$_ }) }
    $pods += @{ id = [int64]$o.id; group = $o.gId; position = (Pos $o.pos); panels = $panels }
    $p = Pos $o.pos; $podFlat += , @($p.x, $p.z)
  }
}
$signs = @($objects.Values | Where-Object { $_.gId -eq 'Sign' -and $_.pos } | ForEach-Object { @{ id = [int64]$_.id; position = (Pos $_.pos); text = $_.text } })

function NearPod($p) { foreach ($f in $podFlat) { if ((($f[0] - $p.x) * ($f[0] - $p.x) + ($f[1] - $p.z) * ($f[1] - $p.z)) -le ($reach * $reach)) { return $true } } return $false }

# ---- extractors, and containers near a base ----
$extractors = @(); $containers = @()
foreach ($o in $objects.Values) {
  if (-not $o.pos) { continue }
  $kind = switch -Regex ($o.gId) { '^OreExtractor' { 'ore' } '^GasExtractor' { 'gas' } '^WaterCollector' { 'water' } '^AlgaeGenerator' { 'algae' } default { $null } }

  if ($kind) {
    $inv = if ($kind -eq 'algae') { @(Secondary $o) } elseif ($o.liId) { @($o.liId) } else { @() }
    $items = @(Tally $inv -Growth:($kind -eq 'algae'))
    $size = 0; foreach ($iid in $inv) { if ($details.ContainsKey([int64]$iid)) { $size = $details[[int64]$iid].size } }
    $count = 0; $ready = 0; foreach ($i in $items) { $count += $i.count; if ($i.ready) { $ready += $i.ready } }
    $product = if ($o.liGrps) { ($o.liGrps -split ',')[0].Trim() } else { $null }
    $of = 0; foreach ($i in $items) { if ($i.id -eq $product) { $of += $i.count } }
    $extractors += @{ id = [int64]$o.id; kind = $kind; group = $o.gId; position = (Pos $o.pos); product = $product; productName = $product
                      size = $size; count = $count; productCount = $of; ready = $ready; items = $items }
  }
  elseif (($o.liId -or $o.siIds) -and (NearPod (Pos $o.pos))) {
    $primary = @(Tally @($o.liId))
    $secondary = @(Tally (Secondary $o) -Growth)
    if ($primary.Count -gt 0 -or $secondary.Count -gt 0) {
      $containers += @{ id = [int64]$o.id; group = $o.gId; position = (Pos $o.pos); items = $primary; secondary = $secondary }
    }
  }
}

# ---- building pieces, for the floor plans: position, turn and panel codes only (a save holds no shapes, so "box" is null) ----
# Unity yaw from the quaternion (x, y, z, w): 2 * atan2(y, w), exact for a piece that only turns about the vertical.
function Yaw($rot) { $q = $rot -split ','; $y = 2 * [Math]::Atan2((Num $q[1]), (Num $q[3])) * 180 / [Math]::PI; [Math]::Round(((($y % 360) + 360) % 360), 1) }
$structures = @()
foreach ($o in $objects.Values) {
  # As the plugin's IsStructure: the T1 aquarium (Aquarium1) is furniture inside a pod, so only the T2 and later count.
  if (-not $o.pos -or $o.gId -notmatch '^(pod|EscapePod|Foundation|VehicleCrafter|Ladder$|Aquarium[2-9])|Platform|dome|lab') { continue }
  $p = Pos $o.pos
  if (-not (NearPod $p)) { continue }
  $panels = @(); if ($o.pnls) { $panels = @($o.pnls -split ',' | ForEach-Object { [int]$_ }) }
  $structures += @{ id = [int64]$o.id; group = $o.gId; position = $p; yaw = (Yaw $o.rot); panels = $panels; box = $null; panelBoxes = $null }
}

$now = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $ci)
$world = [ordered]@{ schemaVersion = 1; pluginVersion = 'save'; updatedAt = $now; inWorld = $true; planetId = $player.planetId
                     pods = $pods; signs = $signs; containers = $containers; extractors = $extractors; structures = $structures }

$rot = $player.playerRotation -split ','
# Unity yaw from the quaternion (x, y, z, w): 2 * atan2(y, w) is exact for a body that only turns about the vertical.
$yaw = 2 * [Math]::Atan2((Num $rot[1]), (Num $rot[3])) * 180 / [Math]::PI
$live = [ordered]@{ schemaVersion = 1; pluginVersion = 'save'; updatedAt = $now; inWorld = $true; planetId = $player.planetId
                    player = [ordered]@{ name = $player.name; position = (Pos $player.playerPosition); yawDegrees = (($yaw % 360) + 360) % 360 } }

New-Item -ItemType Directory -Force -Path $OutFolder | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)

function Stamp { [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $ci) }

function Write-Atomic($name, $doc) {
  $file = Join-Path $OutFolder $name
  [IO.File]::WriteAllText("$file.tmp", ($doc | ConvertTo-Json -Depth 8 -Compress), $utf8)
  Move-Item -Force "$file.tmp" $file
}

"{0} pods, {1} signs, {2} containers near a pod, {3} extractors, {4} building pieces" -f $pods.Count, $signs.Count, $containers.Count, $extractors.Count, $structures.Count

$tick = 0
do {
  # A fresh timestamp each second keeps the dashboard saying LIVE (the world file, like the real one, is refreshed less often).
  $live.updatedAt = Stamp
  Write-Atomic 'live.json' $live
  if ($tick % 5 -eq 0) { $world.updatedAt = Stamp; Write-Atomic 'live-world.json' $world }
  $tick++
  if ($Loop) { Start-Sleep -Seconds 1 }
} while ($Loop)

"wrote $OutFolder"
