using System.Numerics;

namespace RRSOS.PCC.Dashboard
{
    public enum BaseKind
    {
        Outpost,
        Base
    }

    /// <summary>A number of items of one kind (a stored or loose item, by the game's group id).</summary>
    public sealed record ItemCount(string Id, string Name, int Count);

    /// <summary>One base or outpost, with what is stored in it. Where it is relative to the player is worked out at drawing time (see <see cref="BaseView"/>).</summary>
    public sealed class BaseInfo
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
        public BaseKind Kind { get; init; }

        /// <summary>The base's raw world (X, Z).</summary>
        public Vector2 Flat { get; init; }

        /// <summary>Everything stored in containers here, except machines and building parts.</summary>
        public IReadOnlyList<ItemCount> Stored { get; init; } = Array.Empty<ItemCount>();

        /// <summary>Crops in growers that have finished growing.</summary>
        public IReadOnlyList<ItemCount> Ready { get; init; } = Array.Empty<ItemCount>();

        /// <summary>Loose ore, alloy, quartz and rods lying on the ground (the "boneyard").</summary>
        public IReadOnlyList<ItemCount> Loose { get; init; } = Array.Empty<ItemCount>();
    }

    /// <summary>
    /// Works out the bases from the plugin's raw facts, the way RRSOS-PCC does from a save:
    /// a base is a living compartment (a pod) that has an entrance panel (a door); with a connection panel as well it is a
    /// "Base", without one an "Outpost". Everything (containers, loose items) belongs to the nearest base within 100 m.
    /// </summary>
    public sealed class BaseDirectory
    {
        /// <summary>Objects belong to the nearest base within this many metres.</summary>
        public const float OwnershipRadius = 100f;

        private const float SignRange = 6f;
        private const int DoorPanel = 4;
        private const int ConnectionPanel = 2;

        public static readonly BaseDirectory Empty = new(new List<BaseInfo>());

        public IReadOnlyList<BaseInfo> Bases { get; }

        private BaseDirectory(List<BaseInfo> bases) => Bases = bases;

        public static BaseDirectory Build(WorldData world, BaseNames names, ItemCatalog catalog)
        {
            // Sorted by id so that the first time names are handed out, the order does not depend on the game's.
            var pods = world.Pods
                .Where(p => p.Position is not null && IsBasePod(p))
                .OrderBy(p => p.Id)
                .ToList();

            var found = new List<Draft>();

            foreach (var pod in pods)
            {
                var flat = new Vector2((float)pod.Position!.X, (float)pod.Position.Z);
                var kind = pod.Panels.Contains(ConnectionPanel) ? BaseKind.Base : BaseKind.Outpost;
                found.Add(new Draft(pod.Id, flat, kind, names.Resolve(pod.Id, kind, SignNear(world, flat))));
            }

            names.FlushIfDirty();

            foreach (var container in world.Containers)
            {
                if (Nearest(found, container.Position) is not { } owner)
                    continue;

                AddStored(owner.Stored, container.Items, catalog);
                AddStored(owner.Stored, container.Secondary, catalog);

                // Crops are ready to harvest only in growers, and only the ones in their planting tray (the secondary storage).
                if (container.Group.Contains("VegetableGrower", StringComparison.OrdinalIgnoreCase)
                    || container.Group.Contains("Farm1", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var crop in container.Secondary.Where(c => c.Ready > 0))
                        Add(owner.Ready, crop.Id, crop.Label, crop.Ready);
                }
            }

            foreach (var loose in world.Loose)
            {
                // The boneyard is raw material lying around the base (ore, ice, super alloy, quartz, rods). Plants, drones, vehicles and the like are left out.
                if (Nearest(found, loose.Position) is { } owner && IsBoneyardMaterial(catalog, loose.Id))
                    Add(owner.Loose, loose.Id, loose.Label, loose.Count);
            }

            return new BaseDirectory(found.Select(d => d.ToInfo()).ToList());
        }

        /// <summary>A door is what makes a compartment a base. Only single compartments count, as in RRSOS-PCC.</summary>
        private static bool IsBasePod(PodData pod) =>
            (pod.Group.Equals("pod", StringComparison.OrdinalIgnoreCase) || pod.Group.Equals("Escapepod", StringComparison.OrdinalIgnoreCase))
            && pod.Panels.Contains(DoorPanel);

        private static string? SignNear(WorldData world, Vector2 flat) =>
            world.Signs
                .Where(s => s.Position is not null && !string.IsNullOrWhiteSpace(s.Text))
                .Select(s => (Sign: s, Distance: Vector2.Distance(flat, new Vector2((float)s.Position!.X, (float)s.Position.Z))))
                .Where(x => x.Distance <= SignRange)
                .OrderBy(x => x.Distance)
                .Select(x => x.Sign.Text)
                .FirstOrDefault();

        private static Draft? Nearest(List<Draft> bases, Vec3? position)
        {
            if (position is null)
                return null;

            var flat = new Vector2((float)position.X, (float)position.Z);
            Draft? best = null;
            var bestDistance = OwnershipRadius * OwnershipRadius;

            foreach (var candidate in bases)
            {
                var distance = Vector2.DistanceSquared(flat, candidate.Flat);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>What may lie in the boneyard: ores (which includes ice and uranium), super alloy, quartz and rods. Types are those of RRSOS-PCC's table.</summary>
        private static bool IsBoneyardMaterial(ItemCatalog catalog, string id) =>
            catalog.TypeOf(id) is ItemType.Ore or ItemType.Alloy or ItemType.Quartz or ItemType.Rod;

        private static bool IsMachineOrBuildingPart(ItemCatalog catalog, string id) =>
            catalog.CategoryOf(id) is ItemCategory.Machine or ItemCategory.BasePart;

        private static void AddStored(Dictionary<string, (string Name, int Count)> into, List<StoredData> items, ItemCatalog catalog)
        {
            foreach (var item in items)
            {
                if (!IsMachineOrBuildingPart(catalog, item.Id))
                    Add(into, item.Id, item.Label, item.Count);
            }
        }

        private static void Add(Dictionary<string, (string Name, int Count)> into, string id, string name, int count) =>
            into[id] = into.TryGetValue(id, out var have) ? (have.Name, have.Count + count) : (name, count);

        // Counts pile up here while the containers are walked, and become the base's lists at the end.
        private sealed class Draft
        {
            public Draft(long id, Vector2 flat, BaseKind kind, string name)
            {
                Id = id;
                Flat = flat;
                Kind = kind;
                Name = name;
            }

            public long Id { get; }
            public Vector2 Flat { get; }
            public BaseKind Kind { get; }
            public string Name { get; }
            public Dictionary<string, (string Name, int Count)> Stored { get; } = new();
            public Dictionary<string, (string Name, int Count)> Ready { get; } = new();
            public Dictionary<string, (string Name, int Count)> Loose { get; } = new();

            public BaseInfo ToInfo() => new()
            {
                Id = Id,
                Name = Name,
                Kind = Kind,
                Flat = Flat,
                Stored = ToList(Stored),
                Ready = ToList(Ready),
                Loose = ToList(Loose)
            };

            private static List<ItemCount> ToList(Dictionary<string, (string Name, int Count)> counts) =>
                counts.Select(p => new ItemCount(p.Key, p.Value.Name, p.Value.Count))
                      .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                      .ToList();
        }
    }

    /// <summary>A base as seen from where the player stands now.</summary>
    public sealed class BaseView
    {
        public BaseView(BaseInfo info, Vector2 playerFlat)
        {
            Info = info;
            Distance = Vector2.Distance(info.Flat, playerFlat);
            Direction = Compass.GetCompassDirection(playerFlat, info.Flat);
        }

        public BaseInfo Info { get; }
        public long Id => Info.Id;
        public string Name => Info.Name;
        public BaseKind Kind => Info.Kind;
        public Vector2 Flat => Info.Flat;
        public float Distance { get; }
        public string Direction { get; }
        public string DisplayDistance => $"{(int)Distance}m";
    }
}
