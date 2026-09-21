<#
  Writes a realistic FAKE live.json so the dashboard can be developed and checked without the game.
  It follows docs/contract.md (schema 1). The numbers are made up but shaped like a mid-game save.

  .\tools\sample-live.ps1 -Path .\sample\live.json            one file, then exit
  .\tools\sample-live.ps1 -Path .\sample\live.json -Loop      rewrite it every second (fresh updatedAt, the player walks)
  .\tools\sample-live.ps1 -Path ... -Loop -Scenario menu      "in the main menu" (inWorld false)
  .\tools\sample-live.ps1 -Path ... -Scenario stowed          vehicle in the pocket (no position)
  .\tools\sample-live.ps1 -Path ... -Scenario novehicle       no vehicle unlocked yet

  Point the dashboard at it with:  dotnet run --project src/Dashboard --LiveFile=<that path>
#>
param(
  [Parameter(Mandatory)] [string] $Path,
  [switch] $Loop,
  [ValidateSet('live', 'menu', 'stowed', 'novehicle')] [string] $Scenario = 'live'
)

$ci = [Globalization.CultureInfo]::InvariantCulture

function Items($pairs) { @($pairs | ForEach-Object { @{ id = $_[0]; name = $_[1]; count = $_[2] } }) }

$backpack = Items @(
  @('Iron','Iron',26), @('Cobalt','Cobalt',12), @('Silicon','Silicon',18), @('Magnesium','Magnesium',9),
  @('Aluminium','Aluminium',15), @('Titanium','Titanium',7), @('Iridium','Iridium',3), @('Uranim','Uranium',4),
  @('Alloy','Alloy',22), @('Glass','Glass',6), @('Bioplastic','Bioplastic',8), @('Fertilizer','Fertilizer',5),
  @('OxygenCapsule1','Oxygen Capsule',9), @('WaterBottle1','Water Bottle',6), @('astrofood','Astrofood',4),
  @('Vegetable0Seed','Eggplant Seed',11), @('Vegetable1Seed','Squash Seed',14), @('Vegetable2Seed','Bean Seed',9),
  @('Vegetable3Seed','Mushroom Seed',7), @('Tree1Seed','Linifolia Seed',5), @('Seed10','Flower Seed',12),
  @('ButterflyLarvae','Butterfly Larva',20), @('BeeLarvae','Bee Larva',10), @('FrogEggs','Frog Eggs',8),
  @('Explosive','Explosive',4), @('Blueprint','Blueprint',2), @('CircuitBoard1','Circuit Board',6),
  @('FusionEnergyCell','Fusion Energy Cell',1), @('WardenKey','Warden Key',5), @('Rod-iridium','Iridium Rod',3),
  @('FuseEnergy1','Energy Fuse',2), @('FuseOxygen1','Oxygen Fuse',1), @('Bacteria','Bacteria Sample',13)
)
$gear = Items @(
  @('Jetpack3','Jetpack T3',1), @('BootsSpeed3','Boots T3',1), @('MultiToolMineSpeed4','Multitool T4',1),
  @('MultiToolDeconstruct3','Deconstructor T3',1), @('MultiToolLight3','Light T3',1), @('OxygenTank4','Oxygen Tank T4',1),
  @('HudCompass','Compass Chip',1), @('MapChip','Map Chip',1), @('HudChipCleanConstruction','Clean Construction Chip',1),
  @('Backpack6','Backpack T6',1)
)
$trunk = Items @(
  @('Iron','Iron',60), @('Alloy','Alloy',34), @('Cobalt','Cobalt',20), @('WaterBottle1','Water Bottle',12),
  @('OxygenCapsule1','Oxygen Capsule',15), @('Container1','Storage Container',3), @('Explosive','Explosive',8)
)
$vgear = Items @(
  @('VehicleBeacon1','Vehicle Beacon T1',1), @('VehicleLights2','Vehicle Lights T2',1), @('VehicleEquipmentSize2','Vehicle Equipment Size T2',1),
  @('VehicleOxygen1','Vehicle Oxygen T1',1), @('VehicleInventorySize3','Vehicle Inventory Size T3',1), @('VehicleSpeed3','Vehicle Speed T3',1)
)

function Unit($value, $inc, $dec = 0) { @{ value = $value; increasePerSec = $inc; decreasePerSec = $dec } }

function Build([int] $tick) {
  $walk = $tick * 5.0
  $now = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $ci)
  $doc = [ordered]@{
    schemaVersion = 1; pluginVersion = '0.2.0'; gameVersion = '2.103'; updatedAt = $now
    inWorld = ($Scenario -ne 'menu')
  }
  if ($Scenario -eq 'menu') { return $doc }

  $doc.planetId = 'Prime'
  $doc.player = [ordered]@{
    name = 'dkolln'
    position = @{ x = 875.11 - $walk; y = 28.17; z = 547.94 }
    yawDegrees = 270.0
    vitals = @{ oxygen = 370; health = 69.8; thirst = 60.7; toxic = 0 }
    vitalsMax = @{ oxygen = 370; health = 100; thirst = 100; toxic = 100 }
    backpack = @{ size = 320; items = $backpack }
    equipment = @{ size = 12; items = $gear }
  }
  $doc.planet = [ordered]@{
    units = [ordered]@{
      oxygen = Unit 1581821722624 4.1e9; heat = Unit 781013811200 1.7e9; pressure = Unit 529464524800 9.5e8
      plants = Unit 241768382464 5.2e8; insects = Unit 371836059648 6.8e8; animals = Unit 917431582720 1.1e9
      energy = Unit 0 64600.0 (-41623.05)
    }
    power = [ordered]@{
      producedKw = 64600.0; usedKw = 41623.05
      generators = @(
        @{ id = 'EnergyGenerator1'; count = 2; kw = 2.4 }
        @{ id = 'EnergyGenerator2'; count = 24; kw = 1720.0 }
        @{ id = 'EnergyGenerator3'; count = 9; kw = 5290.0 }
        @{ id = 'EnergyGenerator4'; count = 6; kw = 6100.0 }
        @{ id = 'EnergyGenerator5'; count = 4; kw = 21400.0 }
        @{ id = 'EnergyGenerator6'; count = 3; kw = 30000.0 }
      )
    }
    rockets = [ordered]@{
      oxygen = @{ count = 2; multiplier = 20 }; heat = @{ count = 3; multiplier = 30 }
      pressure = @{ count = 3; multiplier = 30 }; plants = @{ count = 3; multiplier = 37.5 }
      insects = @{ count = 3; multiplier = 45 }; animals = @{ count = 2; multiplier = 35 }
    }
  }
  if ($Scenario -ne 'novehicle') {
    $doc.vehicle = [ordered]@{
      position = if ($Scenario -eq 'stowed') { $null } else { @{ x = 825.14; y = 35.45; z = 664.0 } }
      yawDegrees = if ($Scenario -eq 'stowed') { $null } else { 90.0 }
      trunk = @{ size = 200; items = $trunk }
      gear = @{ size = 8; items = $vgear }
    }
  }
  return $doc
}

$dir = Split-Path -Parent $Path
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$tick = 0
do {
  $json = (Build $tick) | ConvertTo-Json -Depth 8 -Compress
  $tmp = "$Path.tmp"
  [IO.File]::WriteAllText($tmp, $json, (New-Object Text.UTF8Encoding($false)))
  Move-Item -Force $tmp $Path
  $tick++
  if ($Loop) { Start-Sleep -Seconds 1 }
} while ($Loop)

"wrote $Path"
