<#
  Writes a realistic FAKE live.json, and a live-world.json beside it (bases, containers, extractors), so the dashboard
  can be developed and checked without the game. It follows docs/contract.md (schema 1). The numbers are made up but
  shaped like a mid-game save. (tools/save-to-world.ps1 does the same from a real save.)

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
  [ValidateSet('live', 'menu', 'stowed', 'novehicle', 'nobases')] [string] $Scenario = 'live'
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
    schemaVersion = 1; pluginVersion = '0.3.0'; gameVersion = '2.103'; updatedAt = $now
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

# ---- the world file (docs/contract.md, "The world file"): pods, signs, containers, loose items, extractors ----

function P($x, $y, $z) { @{ x = $x; y = $y; z = $z } }
function Stored($pairs) { @($pairs | ForEach-Object { $e = @{ id = $_[0]; name = $_[1]; count = $_[2] }; if ($_.Count -gt 3) { $e.ready = $_[3] }; $e }) }
function Box($id, $group, $pos, $items, $secondary = @()) { @{ id = $id; group = $group; position = $pos; items = @($items); secondary = @($secondary) } }
function Mine($id, $group, $pos, $product, $name, $size, $count) {
  @{ id = $id; kind = 'ore'; group = $group; position = $pos; product = $product; productName = $name; size = $size; count = $count
     productCount = $count; ready = 0; items = @(if ($count -gt 0) { @{ id = $product; name = $name; count = $count } }) }
}

function BuildWorld([int] $tick) {
  $now = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $ci)
  $doc = [ordered]@{ schemaVersion = 1; pluginVersion = '0.3.0'; updatedAt = $now; inWorld = ($Scenario -ne 'menu') }
  if ($Scenario -eq 'menu') { return $doc }

  $doc.planetId = 'Prime'
  $doc.scan = @{ objectsVisited = 31240; frames = 9; workMs = 14.2; worstFrameMs = 2.1 }

  if ($Scenario -eq 'nobases') {
    foreach ($k in 'pods', 'signs', 'containers', 'loose') { $doc[$k] = @() }
  } else {
    # Doors are panel value 4, connections 2. "Main" has both (a Base); the others have a door only (outposts).
    $doc.pods = @(
      @{ id = 204266353; group = 'pod'; position = (P 803 35 613.5);  panels = @(4,2,3,3,5,7) }
      @{ id = 201381949; group = 'pod'; position = (P 803 29 605.5);  panels = @(2,2,2,1,5,7) }
      @{ id = 204342221; group = 'pod'; position = (P 803 29 597.5);  panels = @(2,1,1,1,5,7) }
      @{ id = 206907343; group = 'Pod4x'; position = (P 779 35 570.7); panels = @(2,2,10,8,2,2,10,8,2,1,10,8,1,1,10,8) }
      @{ id = 207570622; group = 'pod'; position = (P 365.5 142 971.5); panels = @(4,1,1,1,5,7) }
      @{ id = 208000001; group = 'pod'; position = (P 940 23 610);    panels = @(4,1,1,1,5,7) }
      @{ id = 208000002; group = 'pod'; position = (P 610 30 320);    panels = @(4,2,3,1,5,7) }
      @{ id = 208000003; group = 'pod'; position = (P 615 30 330);    panels = @(2,2,1,1,5,7) }
      @{ id = 208000004; group = 'pod'; position = (P -795 94 -321);  panels = @(4,1,1,1,5,7) }
      @{ id = 208000005; group = 'pod'; position = (P 120 41 -700);  panels = @(4,2,1,1,5,7) }
    )
    $doc.signs = @(
      @{ id = 1; position = (P 800.4 37.3 617.6);  text = 'Main' }
      @{ id = 2; position = (P 363 144.2 975.6);   text = 'Mountain Wreck' }
      @{ id = 3; position = (P -795.1 94.1 -321.3); text = 'Volcano' }
      @{ id = 4; position = (P 500 30 500);        text = 'A sign far from any base' }
    )
    $doc.containers = @(
      (Box 101 'Container1' (P 806 35 617) (Stored @( @('Iron','Iron',120), @('Silicon','Silicon',64), @('Titanium','Titanium',48), @('Alloy','Alloy',30), @('Aluminium','Aluminium',22) )))
      (Box 102 'Container1' (P 808 35 620) (Stored @( @('Cobalt','Cobalt',40), @('Magnesium','Magnesium',35), @('Sulfur','Sulfur',18), @('Obsidian','Obsidian',12), @('Iridium','Iridium',9) )))
      (Box 103 'Container3' (P 799 35 611) (Stored @( @('WaterBottle1','Water Bottle',24), @('OxygenCapsule1','Oxygen Capsule',31), @('astrofood2','Astrofood',12), @('NitrogenCapsule1','Nitrogen Capsule',8) )))
      (Box 104 'Container1' (P 796 35 622) (Stored @( @('Vegetable0Seed','Eggplant Seed',11), @('Vegetable1Seed','Squash Seed',14), @('Seed1','Flower Seed',7), @('Fertilizer1','Fertilizer',26) )))
      (Box 105 'VegetableGrower2' (P 772 36 579.7) (Stored @(,@('Fertilizer1','Fertilizer',4))) (Stored @( @('Vegetable3Growable','Mushroom Plant',6,4), @('Vegetable1Growable','Squash Plant',3,1) )))
      (Box 106 'Farm1' (P 758.7 35.5 613.7) @() (Stored @( @('Vegetable2Growable','Bean Plant',12,12), @('Vegetable0Growable','Eggplant Plant',5,0) )))
      (Box 107 'Container1' (P 366 142 969) (Stored @( @('Iron','Iron',20), @('WreckServer','Server',3), @('CircuitBoard1','Circuit Board',5) )))
      (Box 108 'Container1' (P 612 30 325) (Stored @( @('Silicon','Silicon',88), @('Uranim','Uranium',6) )))
      (Box 109 'AutoCrafter1' (P 810 35 600) (Stored @(,@('Iron','Iron',10))))
      (Box 110 'RocketReactor' (P -500 -500 -500) (Stored @(,@('RocketReactor','Rocket Reactor',2))))
    )
    $doc.loose = @(
      @{ id = 'Iron'; name = 'Iron'; position = (P 812 34 604); count = 3 }
      @{ id = 'Container1'; name = 'Storage Container'; position = (P 790 35 640); count = 1 }
      @{ id = 'Foundation'; name = 'Foundation'; position = (P 800 35 630); count = 4 }
      @{ id = 'ice'; name = 'Ice'; position = (P 367 141 966); count = 9 }
    )
  }

  $doc.extractors = @(
    (Mine 301 'OreExtractor3' (P 932 23 551) 'Titanium' 'Titanium' 8 8)
    (Mine 302 'OreExtractor3' (P 938 23 551) 'Silicon' 'Silicon' 8 5)
    (Mine 303 'OreExtractor3' (P 944 23 551) 'Silicon' 'Silicon' 8 8)
    (Mine 304 'OreExtractor3' (P 950 23 551) 'Iron' 'Iron' 8 2)
    (Mine 305 'OreExtractor2' (P 700 30 480) 'Sulfur' 'Sulfur' 6 0)
    (Mine 306 'OreExtractor3' (P 690 30 470) 'Iridium' 'Iridium' 8 3)
    @{ id = 310; kind = 'gas'; group = 'GasExtractor2'; position = (P 782.4 35.5 603.3); product = 'OxygenCapsule1'; productName = 'Oxygen Capsule'; size = 6; count = 4; productCount = 4; ready = 0
       items = @(@{ id = 'OxygenCapsule1'; name = 'Oxygen Capsule'; count = 4 }) }
    @{ id = 320; kind = 'water'; group = 'WaterCollector2'; position = (P 749.7 24.8 662.5); product = $null; productName = $null; size = 10; count = 10; productCount = 0; ready = 0
       items = @(@{ id = 'WaterBottle1'; name = 'Water Bottle'; count = 10 }) }
    @{ id = 321; kind = 'water'; group = 'WaterCollector1'; position = (P 740 24.8 670); product = $null; productName = $null; size = 6; count = 1; productCount = 0; ready = 0
       items = @(@{ id = 'WaterBottle1'; name = 'Water Bottle'; count = 1 }) }
    @{ id = 330; kind = 'algae'; group = 'AlgaeGenerator2'; position = (P 747.6 24.8 690.7); product = $null; productName = $null; size = 8; count = 8; productCount = 0; ready = 5
       items = @(@{ id = 'Algae1Growable'; name = 'Algae'; count = 8; ready = 5 }) }
  )
  return $doc
}

function Write-Atomic($file, $doc) {
  $tmp = "$file.tmp"
  [IO.File]::WriteAllText($tmp, ($doc | ConvertTo-Json -Depth 8 -Compress), (New-Object Text.UTF8Encoding($false)))
  Move-Item -Force $tmp $file
}

$dir = Split-Path -Parent $Path
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$worldPath = Join-Path $(if ($dir) { $dir } else { '.' }) 'live-world.json'

$tick = 0
do {
  Write-Atomic $Path (Build $tick)

  # The real plugin writes the world file every few seconds, not every second.
  if ($tick % 5 -eq 0) { Write-Atomic $worldPath (BuildWorld $tick) }

  $tick++
  if ($Loop) { Start-Sleep -Seconds 1 }
} while ($Loop)

"wrote $Path and $worldPath"
