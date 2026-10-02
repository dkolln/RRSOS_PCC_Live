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

    /// <summary>One ingredient a crafter's recipe needs that is nowhere in the base's own storage.</summary>
    public sealed record MissingIngredient(string Id, string Name);

    /// <summary>An AutoCrafter1 with a recipe set, and which of its ingredients (if any) the base cannot currently supply.</summary>
    public sealed record CrafterStatus(long Id, string Product, string ProductName, IReadOnlyList<MissingIngredient> Missing);

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

        /// <summary>The same, leaving out every Container3 that is full of one item (80 of it): a warehouse is hundreds of those, and they bury what else is stored.
        /// <see cref="FullSingleContainers"/> says how many were left out.</summary>
        public IReadOnlyList<ItemCount> StoredWithoutFull { get; init; } = Array.Empty<ItemCount>();

        /// <summary>How many Container3 here are full of one kind of item (see <see cref="BaseDirectory.FullSingleSlots"/>).</summary>
        public int FullSingleContainers { get; init; }

        /// <summary>Crops in growers that have finished growing.</summary>
        public IReadOnlyList<ItemCount> Ready { get; init; } = Array.Empty<ItemCount>();

        /// <summary>Loose ore, alloy, quartz and rods lying on the ground (the "boneyard").</summary>
        public IReadOnlyList<ItemCount> Loose { get; init; } = Array.Empty<ItemCount>();

        /// <summary>The base drawn from above, floor by floor. Null when the world file has no building pieces for it.</summary>
        public FloorPlan? Plan { get; init; }

        /// <summary>Every autocrafter here with a recipe set, and what it is short of, if anything. Only the labelled ones: a freshly
        /// placed crafter with no recipe yet has nothing to report. Empty before plugin 0.10.0.</summary>
        public IReadOnlyList<CrafterStatus> Crafters { get; init; } = Array.Empty<CrafterStatus>();

        /// <summary>How many AutoCrafter1 stand here, recipe or not: whether a factory has been built at all.</summary>
        public int CrafterCount { get; init; }

        /// <summary>True once a factory has been built here.</summary>
        public bool HasFactory => CrafterCount > 0;
    }

    /// <summary>
    /// Works out the bases from the plugin's raw facts, the way RRSOS-PCC does from a save:
    /// a base is a living compartment (a pod) that has an entrance panel (a door); with a connection panel as well it is a
    /// "Base", without one an "Outpost". Everything (containers, loose items) belongs to the nearest base within 100 m.
    /// </summary>
    public sealed class BaseDirectory
    {
        /// <summary>Objects belong to the nearest base within this many metres. Widened from 100m (2026-09-27): a big factory's
        /// farthest platforms can sit past 100m of the base's own pod, which silently dropped them from every list (Stored,
        /// EmptyChests, Crafters alike). The plugin's own reach for reporting a container at all (WorldScan.ReachMeters) is
        /// kept a step ahead of this, so those containers make it to the dashboard in the first place.</summary>
        public const float OwnershipRadius = 200f;

        private const float SignRange = 6f;
        private const int DoorPanel = 4;
        private const int ConnectionPanel = 2;

        public static readonly BaseDirectory Empty = new(new List<BaseInfo>());

        public IReadOnlyList<BaseInfo> Bases { get; }

        private BaseDirectory(List<BaseInfo> bases) => Bases = bases;

        /// <param name="saveObjects">What the last save says lies in the world (see <see cref="SaveLooseService"/>): the boneyard's source.</param>
        /// <param name="book">The game's recipes (plugin 0.9.0's <c>recipes.json</c>), for working out which crafters are short an ingredient. Null skips that (older plugin, or the file is not there yet).</param>
        public static BaseDirectory Build(WorldData world, IEnumerable<SaveObject> saveObjects, BaseNames names, ItemCatalog catalog, RecipeBook? book = null)
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

                owner.Spots.Add((container.Position!, container.Label));
                AddStored(owner.Stored, container.Items, catalog);
                AddStored(owner.Stored, container.Secondary, catalog);

                // The second list leaves out a Container3 that is full of one item.
                if (IsFullSingleChest(container))
                {
                    owner.FullSingle++;
                }
                else
                {
                    AddStored(owner.StoredPlain, container.Items, catalog);
                    AddStored(owner.StoredPlain, container.Secondary, catalog);
                }

                // Crops are ready to harvest only in growers, and only the ones in their planting tray (the secondary storage).
                if (container.Group.Contains("VegetableGrower", StringComparison.OrdinalIgnoreCase)
                    || container.Group.Contains("Farm1", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var crop in container.Secondary.Where(c => c.Ready > 0))
                        Add(owner.Ready, crop.Id, crop.Label, crop.Ready);
                }

                if (IsAutoCrafter(container.Group))
                {
                    owner.CrafterCount++;
                    if (!string.IsNullOrWhiteSpace(container.Label))
                        owner.Crafters.Add((container.Id, container.Label!, LabelName(container, catalog)));
                }
            }

            foreach (var loose in saveObjects)
            {
                // Only this planet's (a save holds every planet, and their coordinates overlap).
                if (world.PlanetHash != 0 && loose.PlanetHash != 0 && loose.PlanetHash != world.PlanetHash)
                    continue;

                // The boneyard is raw material lying around the base (ore, ice, super alloy, quartz, rods). Buildings, plants, drones, vehicles and the like are left out.
                if (IsBoneyardMaterial(catalog, loose.GId) && Nearest(found, loose.Position) is { } owner)
                    Add(owner.Loose, loose.GId, catalog.NameOf(loose.GId), 1);
            }

            // Building pieces go to their nearest base too, for its floor plan.
            foreach (var structure in world.Structures)
            {
                if (Nearest(found, structure.Position) is { } owner)
                    owner.Pieces.Add(structure);
            }

            return new BaseDirectory(found.Select(d => d.ToInfo(book, catalog)).ToList());
        }

        /// <summary>A Container3 (80 slots) holding 80 of one kind of item is "full of the same item": what a warehouse chest looks like once stocked.</summary>
        public const int FullSingleSlots = 80;

        private static bool IsFullSingleChest(ContainerData container) =>
            container.Group.Equals("Container3", StringComparison.OrdinalIgnoreCase)
            && container.Secondary.Count == 0
            && container.Items.Count == 1
            && container.Items[0].Count >= FullSingleSlots;

        private static bool IsAutoCrafter(string group) => group.Equals("AutoCrafter1", StringComparison.OrdinalIgnoreCase);

        // The plugin already sends a display name; fall back to the catalog (an older plugin, or a group the catalog knows better).
        private static string LabelName(ContainerData container, ItemCatalog catalog) =>
            string.IsNullOrWhiteSpace(container.LabelName) ? catalog.NameOf(container.Label!) : container.LabelName!;

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
            public Dictionary<string, (string Name, int Count)> StoredPlain { get; } = new(); // Stored without the Container3 full of one item
            public int FullSingle { get; set; }
            public Dictionary<string, (string Name, int Count)> Ready { get; } = new();
            public Dictionary<string, (string Name, int Count)> Loose { get; } = new();
            public List<StructureData> Pieces { get; } = new();
            public List<(Vec3 Position, string? Item)> Spots { get; } = new();
            public List<(long Id, string Label, string Name)> Crafters { get; } = new();
            public int CrafterCount { get; set; }

            public BaseInfo ToInfo(RecipeBook? book, ItemCatalog catalog)
            {
                var stored = ToList(Stored);
                var have = stored.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

                var crafters = Crafters
                    .Select(c => new CrafterStatus(c.Id, c.Label, c.Name, Shortage(c.Label, book, have, catalog)))
                    .OrderBy(c => c.ProductName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                return new BaseInfo
                {
                    Id = Id,
                    Name = Name,
                    Kind = Kind,
                    Flat = Flat,
                    Stored = stored,
                    StoredWithoutFull = ToList(StoredPlain),
                    FullSingleContainers = FullSingle,
                    Ready = ToList(Ready),
                    Loose = ToList(Loose),
                    Plan = FloorPlan.Build(Pieces, Spots),
                    Crafters = crafters,
                    CrafterCount = CrafterCount
                };
            }

            // What a crafter's own recipe needs that this base cannot currently supply from anywhere in its storage
            // (a crafter itself reads any in-range container, not just its own base's total, but that finer-grained
            // reach is what the Factory tab already plans around; this is the simpler "go get more of this" signal).
            private static IReadOnlyList<MissingIngredient> Shortage(string product, RecipeBook? book, HashSet<string> have, ItemCatalog catalog) =>
                book is not null && book.Recipes.TryGetValue(product, out var recipe)
                    ? recipe.Ingredients.Keys.Where(i => !have.Contains(i)).Select(i => new MissingIngredient(i, catalog.NameOf(i))).ToList()
                    : Array.Empty<MissingIngredient>();

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
