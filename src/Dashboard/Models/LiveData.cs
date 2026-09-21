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
        public VehicleData? Vehicle { get; set; }
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
        /// <summary>Null while the vehicle is stowed (pocket or portal): it has no place in the world.</summary>
        public Vec3? Position { get; set; }
        public double? YawDegrees { get; set; }
        public InventoryData? Trunk { get; set; }
        public InventoryData? Gear { get; set; }
    }
}
