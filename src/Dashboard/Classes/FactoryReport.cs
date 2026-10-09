namespace RRSOS.PCC.Dashboard
{
    public enum CrafterState
    {
        /// <summary>Nothing in its output slots.</summary>
        Empty,
        /// <summary>Some output, with room for more.</summary>
        Working,
        /// <summary>Every output slot is taken: it stops until something takes the product away.</summary>
        Full
    }

    /// <summary>Where an ingredient comes from, in the order the report looks for it.</summary>
    public enum SupplyKind
    {
        /// <summary>Nothing in the factory or on the planet makes or fetches it.</summary>
        None,
        /// <summary>Another autocrafter in the factory makes it.</summary>
        Crafter,
        /// <summary>An extractor or collector on the planet is set to it.</summary>
        Extractor,
        /// <summary>The rocket depot delivers it (assumed, see <see cref="FactoryReport.DepotItems"/>).</summary>
        Depot,
        /// <summary>Grown or bred: crops, algae, honey, silk (assumed, see <see cref="FactoryReport.GrownTypes"/>).</summary>
        Grown
    }

    public enum IngredientState
    {
        /// <summary>Has a supply, and every crafter that needs it finds enough within its reach.</summary>
        Ok,
        /// <summary>Has a supply, but some crafters do not find enough within their reach right now.</summary>
        Dry,
        /// <summary>Has no supply at all.</summary>
        Unsupplied
    }

    /// <param name="Supply">The first supply found (see <see cref="SupplyKind"/>).</param>
    /// <param name="SupplyNote">What the supply is, in words ("3 extractors", "made by a crafter").</param>
    /// <param name="Needed">The most of it any one craft takes.</param>
    /// <param name="Crafters">Every crafter that needs it.</param>
    /// <param name="Short">The crafters among those that find less than a craft's worth within their reach.</param>
    /// <param name="Fix">What to build when it is unsupplied; empty otherwise.</param>
    public sealed record IngredientRow(string Id, string Name, SupplyKind Supply, string SupplyNote, int Needed, IngredientState State,
        IReadOnlyList<string> Crafters, IReadOnlyList<string> Short, string Fix);

    /// <param name="WaitingOn">Names of the ingredients this crafter finds too little of within its reach. Empty when it has all it needs.</param>
    public sealed record CrafterRow(long Id, string ProductName, int Held, int Capacity, CrafterState State, IReadOnlyList<string> WaitingOn);

    /// <summary>
    /// What a factory is missing, which of its crafters are empty and which are full. A crafter pulls from the containers inside its own range, so for every
    /// ingredient of every recipe it asks two things: "can this ever arrive?" (another crafter makes it, an extractor or collector on the planet is set to it,
    /// the rocket depot brings it, or it is grown) and "does this crafter find enough within its reach right now?". A zero-sum factory has no ingredient
    /// without a supply.
    /// </summary>
    public sealed class FactoryReport
    {
        /// <summary>What the rocket depot is assumed to deliver (the owner's word, 2026-10-09). Game ids.</summary>
        public static readonly IReadOnlySet<string> DepotItems = new HashSet<string>(StringComparer.Ordinal) { "Selenium", "Phosphorus", "Minable-Tungsten", "Amber" };

        /// <summary>Item types that are grown or bred rather than made (crops, silk), plus honey: assumed supplied, as the owner said they are. Algae are not: they need an algae collector on the planet.</summary>
        public static readonly IReadOnlySet<ItemType> GrownTypes = new HashSet<ItemType> { ItemType.Vegetable, ItemType.Thread };
        public const string Honey = "honey";

        public const string CrafterGroup = "AutoCrafter1";

        /// <summary>An AutoCrafter1 has 8 output slots, one item each (its inventory "size" in a save).</summary>
        public const int CrafterSlots = 8;

        /// <summary>The autocrafter's reach when the recipe list does not say (its prefab value is 20 m).</summary>
        public const double DefaultRange = 20;

        /// <summary>Two crafters closer than this belong to the same factory.</summary>
        public const double LinkMeters = 15;

        /// <summary>How far above a chest's pivot its body reaches on average, so the range test aims at the middle of the chest (as <see cref="FactoryPlanner"/> does).</summary>
        private const double ChestMiddle = 1.0;

        public IReadOnlyList<CrafterRow> Crafters { get; init; } = Array.Empty<CrafterRow>();
        public IReadOnlyList<IngredientRow> Ingredients { get; init; } = Array.Empty<IngredientRow>();

        /// <summary>Autocrafters in this factory with no recipe picked yet (they are not in <see cref="Crafters"/>).</summary>
        public int NoRecipe { get; init; }

        /// <summary>True when the recipes were not available, so ingredients could not be worked out.</summary>
        public bool NoRecipeBook { get; init; }

        /// <summary>True when an extractor did not say what its drone settings supply (plugin before 0.17.0): it is then counted as supplying its product.</summary>
        public bool SupplyNotReported { get; init; }

        public int Empty => Crafters.Count(c => c.State == CrafterState.Empty);
        public int Full => Crafters.Count(c => c.State == CrafterState.Full);
        public int Working => Crafters.Count(c => c.State == CrafterState.Working);

        public IEnumerable<IngredientRow> Unsupplied => Ingredients.Where(i => i.State == IngredientState.Unsupplied);
        public IEnumerable<IngredientRow> Dry => Ingredients.Where(i => i.State == IngredientState.Dry);

        /// <summary>No ingredient lacks a supply: set and forget would work, given enough extractors and drones.</summary>
        public bool ZeroSum => !NoRecipeBook && !Unsupplied.Any();

        /// <summary>How many autocrafters (with or without a recipe) are in the factory.</summary>
        public int CrafterCount => Crafters.Count + NoRecipe;

        /// <summary>
        /// The factory the player is at: the cluster of autocrafters (each within <see cref="LinkMeters"/> of another) nearest to the player, or the biggest one
        /// when the player's position is not known. Empty when the planet has no autocrafters.
        /// </summary>
        public static IReadOnlyList<ContainerData> FindFactory(WorldData? world, Vec3? player)
        {
            var all = world?.Containers.Where(c => c.Group.Equals(CrafterGroup, StringComparison.OrdinalIgnoreCase) && c.Position is not null).ToList();
            if (all is null || all.Count == 0)
                return Array.Empty<ContainerData>();

            // Single linkage: keep absorbing crafters that are close to one already in the cluster.
            var clusters = new List<List<ContainerData>>();
            var left = new List<ContainerData>(all);
            while (left.Count > 0)
            {
                var cluster = new List<ContainerData> { left[0] };
                left.RemoveAt(0);

                for (var i = 0; i < cluster.Count; i++)
                {
                    for (var j = left.Count - 1; j >= 0; j--)
                    {
                        if (Distance(cluster[i].Position!, left[j].Position!) <= LinkMeters)
                        {
                            cluster.Add(left[j]);
                            left.RemoveAt(j);
                        }
                    }
                }

                clusters.Add(cluster);
            }

            if (player is null)
                return clusters.OrderByDescending(c => c.Count).First();

            return clusters.OrderBy(c => c.Min(m => Distance(m.Position!, player))).First();
        }

        /// <param name="crafters">The factory (see <see cref="FindFactory"/>).</param>
        /// <param name="containers">Every container the plugin reported: what a crafter can reach is taken from these.</param>
        /// <param name="extractors">Every extractor and collector on the planet (drones carry from anywhere).</param>
        public static FactoryReport Build(IReadOnlyList<ContainerData> crafters, IReadOnlyList<ContainerData> containers, RecipeBook? book,
            IEnumerable<ExtractorData> extractors, ItemCatalog catalog)
        {
            var withRecipe = crafters.Where(c => !string.IsNullOrWhiteSpace(c.Label)).ToList();
            var made = withRecipe.Select(c => c.Label!).ToHashSet(StringComparer.Ordinal);
            var fromExtractors = ExtractorSupply(extractors);
            var range = book?.AutoCrafter?.Range ?? DefaultRange;

            // What each crafter finds within its reach: every container's items (the crafters' own outputs too, so one can feed another).
            var reach = new Dictionary<ContainerData, Dictionary<string, int>>();
            foreach (var crafter in withRecipe)
            {
                var found = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var container in containers)
                {
                    if (container.Position is null || ReferenceEquals(container, crafter) || container.Id == crafter.Id
                        || Distance(crafter.Position!, container.Position, ChestMiddle) > range)
                        continue;

                    foreach (var item in container.Items)
                        found[item.Id] = found.TryGetValue(item.Id, out var n) ? n + item.Count : item.Count;
                }

                reach[crafter] = found;
            }

            // What the planet holds of each item in all the containers the plugin reported, in reach or not: tells "nowhere" from "somewhere else".
            var stored = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var container in containers)
                foreach (var item in container.Items)
                    stored[item.Id] = stored.TryGetValue(item.Id, out var n) ? n + item.Count : item.Count;

            // One row per ingredient, over every crafter's recipe.
            var needs = new Dictionary<string, (int Needed, List<ContainerData> Crafters)>(StringComparer.Ordinal);
            if (book is not null)
            {
                foreach (var crafter in withRecipe)
                {
                    if (!book.Recipes.TryGetValue(crafter.Label!, out var recipe))
                        continue;

                    foreach (var (id, count) in recipe.Ingredients)
                    {
                        if (!needs.TryGetValue(id, out var have))
                            needs[id] = have = (0, new List<ContainerData>());

                        have.Crafters.Add(crafter);
                        needs[id] = (Math.Max(have.Needed, count), have.Crafters);
                    }
                }
            }

            // Two items (or crafters) can share a display name (two different mutagens); the id then tells them apart.
            var names = Distinguish(needs.Keys, catalog.NameOf);
            var crafterNames = Distinguish(withRecipe.Select(c => c.Label!), p => NameOf(withRecipe.First(c => c.Label == p), catalog));

            var rows = new List<IngredientRow>();
            foreach (var (id, need) in needs)
            {
                var (supply, note) = SupplyOf(id, made, fromExtractors, catalog);
                var lacking = need.Crafters.Where(c => !reach[c].TryGetValue(id, out var n) || n < NeededBy(c, id, book)).ToList();
                var state = supply == SupplyKind.None ? IngredientState.Unsupplied : lacking.Count > 0 ? IngredientState.Dry : IngredientState.Ok;

                rows.Add(new IngredientRow(id, names[id], supply, note, need.Needed, state,
                    need.Crafters.Select(c => crafterNames[c.Label!]).OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList(),
                    lacking.Select(c => crafterNames[c.Label!]).OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList(),
                    state switch
                    {
                        IngredientState.Unsupplied => (fromExtractors.NotCollected.Contains(id)
                            ? "A source (extractor, collector or harvesting robot) is set to " + catalog.NameOf(id) + ", but its drone supply is not: add " + catalog.NameOf(id) + " to what it supplies"
                            : FixFor(id, book, catalog)) + (stored.TryGetValue(id, out var left) && left > 0 ? $". {left} still in storage, but nothing refills it" : ""),
                        IngredientState.Dry => DryHint(id, supply, lacking, containers, range, stored.TryGetValue(id, out var have) ? have : 0, need.Needed),
                        _ => ""
                    }));
            }

            var rowById = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
            var crafterRows = withRecipe
                .Select(c =>
                {
                    var waiting = book is not null && book.Recipes.TryGetValue(c.Label!, out var recipe)
                        ? recipe.Ingredients.Where(i => !reach[c].TryGetValue(i.Key, out var n) || n < i.Value).Select(i => rowById[i.Key].Name)
                            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList()
                        : new List<string>();
                    var held = c.Items.Sum(i => i.Count);
                    var state = held >= CrafterSlots ? CrafterState.Full : held <= 0 ? CrafterState.Empty : CrafterState.Working;
                    return new CrafterRow(c.Id, crafterNames[c.Label!], held, CrafterSlots, state, waiting);
                })
                .OrderBy(c => c.ProductName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new FactoryReport
            {
                Crafters = crafterRows,
                Ingredients = rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                NoRecipe = crafters.Count - withRecipe.Count,
                NoRecipeBook = book is null,
                SupplyNotReported = fromExtractors.SupplyNotReported
            };
        }

        // Why a crafter that has a supply still finds too little. The first thing to tell is whether a container for the item stands within the crafter's reach (a
        // demand chest, labelled or not): if one does and it is empty, the drones are bringing less than the crafters use, so the source is the thing to look at.
        private static string DryHint(string id, SupplyKind supply, List<ContainerData> lacking, IReadOnlyList<ContainerData> containers, double range, int stored, int needed)
        {
            bool IsFor(ContainerData k) => !k.Group.Equals(CrafterGroup, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(k.Label, id, StringComparison.Ordinal) || k.Demand?.Contains(id, StringComparer.Ordinal) == true);

            var chests = containers.Where(k => k.Position is not null && IsFor(k)).ToList();
            var held = 0;
            var everyone = lacking.Count > 0;
            foreach (var crafter in lacking)
            {
                var near = chests.Where(k => Distance(crafter.Position!, k.Position!, ChestMiddle) <= range).ToList();
                if (near.Count == 0)
                    everyone = false;
                else
                    held = Math.Max(held, near.Sum(k => k.Items.Where(i => i.Id == id).Sum(i => i.Count)));
            }

            if (everyone)
            {
                var chest = (lacking.Count == 1 ? "A container for it is within reach" : "A container for it is within reach of all of them") + (held == 0 ? ", but it is empty." : $", but it holds only {held}.");
                return chest + " " + supply switch
                {
                    SupplyKind.Extractor => "More extractors may be required.",
                    SupplyKind.Depot => "Please verify the rocket depot supply is functioning properly.",
                    SupplyKind.Crafter => "The crafter that makes it may need more of what it uses, or a second one.",
                    _ => "More producers may be required."
                };
            }

            return stored >= needed
                ? $"{stored} in storage on this planet, but not within reach of {(lacking.Count == 1 ? "that crafter" : "those crafters")} (needs a demand chest within {range:0} m)"
                : "Not enough in storage on this planet yet";
        }

        private static int NeededBy(ContainerData crafter, string ingredient, RecipeBook? book) =>
            book is not null && book.Recipes.TryGetValue(crafter.Label!, out var recipe) && recipe.Ingredients.TryGetValue(ingredient, out var n) ? n : 1;

        private static string NameOf(ContainerData crafter, ItemCatalog catalog) =>
            string.IsNullOrWhiteSpace(crafter.LabelName) ? catalog.NameOf(crafter.Label!) : crafter.LabelName!;

        private static double Distance(Vec3 a, Vec3 b, double bAbove = 0)
        {
            var dx = a.X - b.X;
            var dy = a.Y - (b.Y + bAbove);
            var dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // A name for each id; where two ids would show the same name, "Name (id)".
        private static Dictionary<string, string> Distinguish(IEnumerable<string> ids, Func<string, string> nameOf)
        {
            var list = ids.Distinct(StringComparer.Ordinal).Select(id => (Id: id, Name: nameOf(id))).ToList();
            var shared = list.GroupBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            return list.ToDictionary(x => x.Id, x => shared.Contains(x.Name) ? $"{x.Name} ({x.Id})" : x.Name, StringComparer.Ordinal);
        }

        /// <summary>What the planet's extractors deliver: <see cref="Count"/> how many machines per item that are set to it AND told to supply it to the drones;
        /// <see cref="NotCollected"/> the items some machine is set to but not told to supply, which feed nothing.</summary>
        private sealed class ExtractorSupplies
        {
            public Dictionary<string, int> Count { get; } = new(StringComparer.Ordinal);
            public HashSet<string> NotCollected { get; } = new(StringComparer.Ordinal);
            public bool SupplyNotReported { get; set; }
        }

        // What each extractor or collector on the planet delivers, and how many do. An ore or gas extractor says its product; when it does not, what it
        // holds tells (it only ever holds its own product). Setting a product is not enough: the machine's drone settings must supply it too, or nothing
        // takes it away. A plugin that does not report the settings (before 0.17.0) leaves them unknown, and the machine counts.
        private static ExtractorSupplies ExtractorSupply(IEnumerable<ExtractorData> extractors)
        {
            var result = new ExtractorSupplies();

            void Add(ExtractorData e, string id)
            {
                if (e.Supply is null)
                {
                    result.SupplyNotReported = true;
                }
                else if (!e.Supply.Contains(id, StringComparer.Ordinal))
                {
                    result.NotCollected.Add(id);
                    return;
                }

                result.Count[id] = result.Count.TryGetValue(id, out var n) ? n + 1 : 1;
            }

            foreach (var e in extractors)
            {
                switch (e.Kind)
                {
                    case "ore":
                    case "gas":
                    case "harvester": // a harvesting robot set to an item (common larvae...) is a source of it just the same
                        if (!string.IsNullOrEmpty(e.Product))
                            Add(e, e.Product);
                        else
                            foreach (var id in e.Items.Select(i => i.Id).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal))
                                Add(e, id);
                        break;
                    case "water":
                        Add(e, "WaterBottle1");
                        break;
                    case "algae":
                        // The plants sit in a secondary inventory the plugin does not read: counted as before (and algae are assumed grown anyway).
                        result.Count["Algae1Seed"] = result.Count.TryGetValue("Algae1Seed", out var algae) ? algae + 1 : 1;
                        break;
                }
            }

            return result;
        }

        private static (SupplyKind, string) SupplyOf(string id, HashSet<string> made, ExtractorSupplies fromExtractors, ItemCatalog catalog)
        {
            if (made.Contains(id))
                return (SupplyKind.Crafter, "made by a crafter here");

            if (fromExtractors.Count.TryGetValue(id, out var count))
                return (SupplyKind.Extractor, count == 1 ? "1 source" : count + " sources");

            if (DepotItems.Contains(id))
                return (SupplyKind.Depot, "rocket depot");

            if (GrownTypes.Contains(catalog.TypeOf(id)) || id.Equals(Honey, StringComparison.OrdinalIgnoreCase))
                return (SupplyKind.Grown, "grown");

            return (SupplyKind.None, "");
        }

        private static string FixFor(string id, RecipeBook? book, ItemCatalog catalog)
        {
            if (catalog.TypeOf(id) == ItemType.Ore)
                return "Needs an extractor set to " + catalog.NameOf(id) + ", if this planet has it";

            if (catalog.TypeOf(id) == ItemType.AlgaeSeed)
                return "There is no algae collector on this planet: build one (an Algae Generator) and set its drone supply to " + catalog.NameOf(id);

            if (catalog.TypeOf(id) is ItemType.GasCanister or ItemType.NitrogenCapsule)
                return "Needs a gas extractor set to " + catalog.NameOf(id) + ", if this planet has it";

            if (book is not null && book.Recipes.TryGetValue(id, out var recipe))
                return RecipeBook.CraftedByAutocrafter(recipe)
                    ? "Add a crafter making " + catalog.NameOf(id)
                    : "Only made in " + string.Join(" / ", recipe.CraftableIn) + ", which an autocrafter cannot use";

            return "Nothing here can make or extract it";
        }
    }
}
