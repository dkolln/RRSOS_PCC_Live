namespace RRSOS.PCC.Dashboard
{
    // The shape of live.json (see docs/contract.md). Every field is optional: the plugin reads each
    // section on its own and writes null for one it could not read, so the page has to cope with any of them missing.

    public sealed class LiveData
    {
        public int SchemaVersion { get; set; }
        public string? PluginVersion { get; set; }
        public string? GameVersion { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool InWorld { get; set; }
        public string? PlanetId { get; set; }
        public PlayerData? Player { get; set; }
        public PlanetData? Planet { get; set; }
        /// <summary>The first truck only; kept for plugins before 0.7.0. Use <see cref="AllVehicles"/>.</summary>
        public VehicleData? Vehicle { get; set; }

        /// <summary>Every truck, oldest first (plugin 0.7.0 and later).</summary>
        public List<VehicleData>? Vehicles { get; set; }

        /// <summary>Every truck, from whichever of the two the plugin wrote.</summary>
        public IReadOnlyList<VehicleData> AllVehicles =>
            Vehicles is { Count: > 0 } all ? all : Vehicle is { } one ? new[] { one } : Array.Empty<VehicleData>();

        public DronesData? Drones { get; set; }

        /// <summary>Each Transmission Antenna's spinning dish (plugin 0.6.0 and later). Null or empty when there are none.</summary>
        public List<AntennaData>? Antennas { get; set; }
    }

    public sealed class Vec3
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public sealed class PlayerData
    {
        public string? Name { get; set; }
        public Vec3? Position { get; set; }
        public double YawDegrees { get; set; }
        public Vitals? Vitals { get; set; }
        public Vitals? VitalsMax { get; set; }
        public InventoryData? Backpack { get; set; }
        public InventoryData? Equipment { get; set; }
    }

    public sealed class Vitals
    {
        public double? Oxygen { get; set; }
        public double? Health { get; set; }
        public double? Thirst { get; set; }
        public double? Toxic { get; set; }
    }

    public sealed class InventoryData
    {
        public int Size { get; set; }
        public List<ItemData> Items { get; set; } = new();
    }

    public sealed class ItemData
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public int Count { get; set; }

        public string Label => string.IsNullOrWhiteSpace(Name) ? Id : Name!;
    }

    public sealed class PlanetData
    {
        public Dictionary<string, UnitData> Units { get; set; } = new();
        public PowerData? Power { get; set; }
        public Dictionary<string, RocketData> Rockets { get; set; } = new();
    }

    public sealed class UnitData
    {
        public double Value { get; set; }
        public double IncreasePerSec { get; set; }
        public double DecreasePerSec { get; set; }
    }

    public sealed class PowerData
    {
        public double ProducedKw { get; set; }
        public double UsedKw { get; set; }
        public List<GeneratorData> Generators { get; set; } = new();
    }

    public sealed class GeneratorData
    {
        public string Id { get; set; } = "";
        public int Count { get; set; }
        public double Kw { get; set; }
    }

    public sealed class RocketData
    {
        public int Count { get; set; }
        public double Multiplier { get; set; }
    }

    public sealed class VehicleData
    {
        /// <summary>The game's id for the truck (plugin 0.7.0 and later; 0 before).</summary>
        public int Id { get; set; }

        /// <summary>Null while the vehicle is stowed (pocket or portal): it has no place in the world.</summary>
        public Vec3? Position { get; set; }
        public double? YawDegrees { get; set; }
        public InventoryData? Trunk { get; set; }
        public InventoryData? Gear { get; set; }
    }

    /// <summary>
    /// A Transmission Antenna's dish: its compass heading (0 north, 90 east) at <see cref="SampledAtMs"/> (Unix time,
    /// milliseconds), and how fast it turns, clockwise from above (0 while the game is paused). From these its heading at
    /// any moment after is heading + rate × seconds since.
    /// </summary>
    public sealed class AntennaData
    {
        public int Id { get; set; }
        public Vec3? Position { get; set; }
        public double Heading { get; set; }
        public double DegreesPerSecond { get; set; }
        public long SampledAtMs { get; set; }
    }

    /// <summary>The drones that are in the air right now. Drones docked in a station are in the world file, as part of the station.</summary>
    public sealed class DronesData
    {
        public List<DroneData> Flying { get; set; } = new();
    }

    public sealed class DroneData
    {
        public int Id { get; set; }
        public string? Group { get; set; }
        public string? Name { get; set; }
        public Vec3? Position { get; set; }
        public double YawDegrees { get; set; }

        /// <summary>Metres per second.</summary>
        public double Speed { get; set; }

        /// <summary>The game's task state: NotAttributed, ToSupply, ToDemand, Loading, Unloading, Done; or "Returning" when it has no task and is heading for a station.</summary>
        public string? State { get; set; }

        public InventoryData? Cargo { get; set; }

        /// <summary>What it was sent to carry, and where from and to (null when it has no task).</summary>
        public NamedThing? Moving { get; set; }
        public PlaceData? From { get; set; }
        public PlaceData? To { get; set; }
    }

    public sealed class NamedThing
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string Label => string.IsNullOrWhiteSpace(Name) ? Id ?? "?" : Name!;
    }

    public sealed class PlaceData
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public Vec3? Position { get; set; }
        public string Label => string.IsNullOrWhiteSpace(Name) ? Id ?? "?" : Name!;
    }
}
