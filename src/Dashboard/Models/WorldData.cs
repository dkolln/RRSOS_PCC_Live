namespace RRSOS.PCC.Dashboard
{
    // The shape of live-world.json (see docs/contract.md, "The world file"). The plugin reports raw facts about the
    // game's placed objects; which of them are bases, and what belongs to which base, is worked out here.

    public sealed class WorldData
    {
        public int SchemaVersion { get; set; }
        public string? PluginVersion { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool InWorld { get; set; }
        public string? PlanetId { get; set; }
        public ScanData? Scan { get; set; }
        public List<PodData> Pods { get; set; } = new();
        public List<SignData> Signs { get; set; } = new();
        public List<ContainerData> Containers { get; set; } = new();
        public List<LooseData> Loose { get; set; } = new();
        public List<ExtractorData> Extractors { get; set; } = new();
        public List<DroneStationData> DroneStations { get; set; } = new();

        /// <summary>Building pieces near the pods, with their shapes (plugin 0.4.0 and later), for the floor plans.</summary>
        public List<StructureData> Structures { get; set; } = new();

        /// <summary>
        /// The plugin writes null for a list the game gave it none of (a pod with no panels, say), and a null in the file
        /// replaces the empty list above. Put the empty lists back, so nothing downstream has to check.
        /// </summary>
        public WorldData Normalize()
        {
            Pods ??= new();
            Signs ??= new();
            Containers ??= new();
            Loose ??= new();
            Extractors ??= new();
            DroneStations ??= new();
            Structures ??= new();

            Structures.RemoveAll(s => s is null);
            foreach (var structure in Structures)
            {
                structure.Group ??= "";
                structure.Panels ??= new();
                structure.PanelBoxes?.RemoveAll(p => p is null);
            }

            Pods.RemoveAll(p => p is null);
            Signs.RemoveAll(s => s is null);
            Containers.RemoveAll(c => c is null);
            Loose.RemoveAll(l => l is null);
            Extractors.RemoveAll(e => e is null);
            DroneStations.RemoveAll(d => d is null);

            foreach (var station in DroneStations)
            {
                station.Group ??= "";
                station.Items = Clean(station.Items);
            }

            foreach (var pod in Pods)
            {
                pod.Panels ??= new();
                pod.Group ??= "";
            }

            foreach (var container in Containers)
            {
                container.Group ??= "";
                container.Items = Clean(container.Items);
                container.Secondary = Clean(container.Secondary);
            }

            foreach (var loose in Loose)
                loose.Id ??= "";

            foreach (var extractor in Extractors)
            {
                extractor.Kind ??= "";
                extractor.Group ??= "";
                extractor.Items = Clean(extractor.Items);
            }

            return this;
        }

        private static List<StoredData> Clean(List<StoredData>? items)
        {
            items ??= new();
            items.RemoveAll(i => i is null);

            foreach (var item in items)
                item.Id ??= "";

            return items;
        }
    }

    /// <summary>How much work the plugin's last pass took (a check that reading the world costs the game nothing).</summary>
    public sealed class ScanData
    {
        public int ObjectsVisited { get; set; }
        public int Frames { get; set; }
        public double WorkMs { get; set; }
        public double WorstFrameMs { get; set; }
    }

    /// <summary>A living compartment. <see cref="Panels"/> lists what fills each of its six sides (4 is a door, 2 a connection).</summary>
    public sealed class PodData
    {
        public int Id { get; set; }
        public string Group { get; set; } = "";
        public Vec3? Position { get; set; }
        public List<int> Panels { get; set; } = new();
    }

    /// <summary>
    /// A building piece (a pod of any shape, a foundation, a platform, a dome, a lab or a ladder). <see cref="Box"/> and
    /// <see cref="PanelBoxes"/> are measured in the piece's own frame: metres from <see cref="Position"/>, before it turns
    /// by <see cref="Yaw"/>. Both are null when the plugin had nothing to measure (and always, from a save).
    /// </summary>
    public sealed class StructureData
    {
        public int Id { get; set; }
        public string Group { get; set; } = "";
        public Vec3? Position { get; set; }

        /// <summary>Unity yaw in degrees (a turn about the vertical, clockwise from above).</summary>
        public double Yaw { get; set; }

        /// <summary>What fills each panel slot, as in the save (see <see cref="PanelCodes"/>).</summary>
        public List<int> Panels { get; set; } = new();

        public BoxData? Box { get; set; }

        /// <summary>The piece's largest flat slab, and any level with it (plugin 0.4.1 and later): a platform's deck, whose top is the deck's height.</summary>
        public BoxData? DeckBox { get; set; }

        public List<PanelBoxData>? PanelBoxes { get; set; }
    }

    public sealed class BoxData
    {
        public Vec3? Min { get; set; }
        public Vec3? Max { get; set; }
    }

    /// <summary>One panel of a piece: its kind (1 wall, 2 floor, 3 angled floor), what fills it (<see cref="PanelCodes"/>), and its box.</summary>
    public sealed class PanelBoxData
    {
        public int Type { get; set; }
        public int Sub { get; set; }
        public bool Ceiling { get; set; }
        public Vec3? Min { get; set; }
        public Vec3? Max { get; set; }
    }

    /// <summary>The game's panel codes (DataConfig.BuildPanelSubType), as the save's "pnls" and the plugin write them.</summary>
    public static class PanelCodes
    {
        public const int WallPlain = 1;
        public const int WallCorridor = 2;
        public const int WallGlass = 3;
        public const int WallDoor = 4;
        public const int WallLab = 9;
        public const int WallInside = 11;
        public const int WallWaterLife = 13;

        public const int TypeWall = 1;
    }

    public sealed class SignData
    {
        public int Id { get; set; }
        public Vec3? Position { get; set; }
        public string? Text { get; set; }
    }

    /// <summary>One kind of item and how many, in a container or an extractor. <see cref="Ready"/> counts crops that have finished growing.</summary>
    public sealed class StoredData
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public int Count { get; set; }
        public int Ready { get; set; }

        public string Label => string.IsNullOrWhiteSpace(Name) ? Id : Name!;
    }

    public sealed class ContainerData
    {
        public int Id { get; set; }
        public string Group { get; set; } = "";
        public Vec3? Position { get; set; }
        public List<StoredData> Items { get; set; } = new();
        public List<StoredData> Secondary { get; set; } = new();
    }

    /// <summary>Items lying on the ground, merged when they are the same kind within a few metres.</summary>
    public sealed class LooseData
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public Vec3? Position { get; set; }
        public int Count { get; set; }

        public string Label => string.IsNullOrWhiteSpace(Name) ? Id : Name!;
    }

    /// <summary>A drone station: where it is, how many drones are docked in it, and what its storage holds.</summary>
    public sealed class DroneStationData
    {
        public int Id { get; set; }
        public string Group { get; set; } = "";
        public string? Name { get; set; }
        public Vec3? Position { get; set; }

        /// <summary>Slots in its storage, and how many drones are in it.</summary>
        public int Size { get; set; }
        public int Docked { get; set; }

        public List<StoredData> Items { get; set; } = new();
    }

    public sealed class ExtractorData
    {
        public int Id { get; set; }

        /// <summary>"ore", "gas", "water" or "algae".</summary>
        public string Kind { get; set; } = "";

        public string Group { get; set; } = "";
        public Vec3? Position { get; set; }

        /// <summary>What an ore or gas extractor is set to produce (its group id and display name). Null for water and algae.</summary>
        public string? Product { get; set; }
        public string? ProductName { get; set; }

        /// <summary>Slots in its storage.</summary>
        public int Size { get; set; }

        /// <summary>Everything in its storage, and how much of that is the product.</summary>
        public int Count { get; set; }
        public int ProductCount { get; set; }

        /// <summary>Algae generators only: how many algae have finished growing.</summary>
        public int Ready { get; set; }

        public List<StoredData> Items { get; set; } = new();
    }
}
