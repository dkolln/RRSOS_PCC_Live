using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>One recipe of the game's <c>recipes.json</c> (plugin 0.9.0): what an object is made of, and where it can be crafted.</summary>
    public sealed record GameRecipe(string Id, string Kind, IReadOnlyDictionary<string, int> Ingredients, IReadOnlyList<string> CraftableIn, string Category, bool HideInCrafter = false);

    /// <summary>A machine that crafts, from <c>recipes.json</c>: for an autocrafter its reach in metres and how often it crafts.</summary>
    public sealed record GameMachine(string Id, string Crafts, double CraftTime, double? Range, double? CraftEverySec);

    public sealed record RecipeBook(IReadOnlyDictionary<string, GameRecipe> Recipes, IReadOnlyList<GameMachine> Machines)
    {
        // The crafting places an autocrafter can use (ActionGroupSelectorAutoCrafter): craft stations, drone, oven, quartz, toxic refinement and the bio lab.
        // Not the incubator, the genetic machines, the rocket, the vehicle crafter or the departure platform.
        private static readonly HashSet<string> AutoPlaces = new(StringComparer.Ordinal)
        {
            "CraftStationT1", "CraftStationT2", "CraftStationT3", "CraftDroneT1", "CraftOvenT1", "CraftQuartzT1", "CraftToxicRefinementT1", "CraftBioLab"
        };

        /// <summary>The autocrafter that reaches the furthest and crafts (AutoCrafter1), not the incubator one.</summary>
        public GameMachine? AutoCrafter => Machines.FirstOrDefault(m => m.Range is > 0 && m.Id.StartsWith("AutoCrafter", StringComparison.Ordinal))
                                           ?? Machines.FirstOrDefault(m => m.Range is > 0);

        public static bool CraftedByAutocrafter(GameRecipe r) =>
            r.Kind == "item" && r.Ingredients.Count > 0 && r.Category != "Equipment" && !r.HideInCrafter && r.CraftableIn.Any(AutoPlaces.Contains);

        /// <summary>Every item an autocrafter can be set to (the game's own rule, without its unlock checks), by id.</summary>
        public IReadOnlyList<GameRecipe> Buildable() => Recipes.Values.Where(CraftedByAutocrafter).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();

        /// <summary>Reads <c>recipes.json</c>. Returns null when the text is not that file.</summary>
        public static RecipeBook? Parse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var recipes = new Dictionary<string, GameRecipe>(StringComparer.Ordinal);

                foreach (var r in root.GetProperty("recipes").EnumerateArray())
                {
                    var id = r.GetProperty("id").GetString() ?? "";
                    var ingredients = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (r.TryGetProperty("ingredients", out var ing))
                        foreach (var p in ing.EnumerateObject())
                            ingredients[p.Name] = p.Value.GetInt32();

                    var craftable = r.TryGetProperty("craftableIn", out var cr) ? cr.EnumerateArray().Select(e => e.GetString() ?? "").ToList() : new List<string>();
                    recipes[id] = new GameRecipe(id, r.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "", ingredients, craftable,
                        r.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "", r.TryGetProperty("hideInCrafter", out var h) && h.ValueKind == JsonValueKind.True);
                }

                var machines = new List<GameMachine>();
                if (root.TryGetProperty("machines", out var ms))
                    foreach (var m in ms.EnumerateArray())
                    {
                        double? range = null, every = null;
                        if (m.TryGetProperty("autocrafter", out var a))
                        {
                            range = a.TryGetProperty("range", out var rg) ? rg.GetDouble() : null;
                            every = a.TryGetProperty("craftEverySec", out var ev) ? ev.GetDouble() : null;
                        }

                        machines.Add(new GameMachine(m.GetProperty("id").GetString() ?? "", m.TryGetProperty("crafts", out var cf) ? cf.GetString() ?? "" : "",
                            m.TryGetProperty("craftTime", out var ct) ? ct.GetDouble() : 0, range, every));
                    }

                return new RecipeBook(recipes, machines);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }
    }

    /// <summary>A chest of the warehouse: what it is labelled with (its item filter, or null for a tank), and where it stands.</summary>
    public sealed record FactoryChest(long Id, string? Item, double X, double Y, double Z);

    /// <summary>How a product would be made, from the best answer the planner found.</summary>
    public enum FactoryMode
    {
        /// <summary>An autocrafter can stand where every ingredient chest is inside its reach.</summary>
        InRange,
        /// <summary>An autocrafter stands where most ingredients are in reach and a local chest of its own would demand the rest (not built yet).</summary>
        LocalChest,
        /// <summary>The recipe is made in a place an autocrafter cannot use (incubator, genetic machines, ...).</summary>
        OtherMachine,
        /// <summary>The game has no recipe for it.</summary>
        NoRecipe
    }

    /// <param name="Spot">Where the crafter would stand (its pivot), for the two autocrafter modes.</param>
    /// <param name="Reached">Ingredients the crafter reaches from there.</param>
    /// <param name="Missing">Ingredients it does not reach: a local chest would demand these.</param>
    /// <param name="Farthest">The longest distance from the spot to a reached ingredient chest, in metres.</param>
    public sealed record FactoryProduct(string Product, FactoryMode Mode, IReadOnlyDictionary<string, int> Ingredients, (double X, double Y, double Z)? Spot,
        IReadOnlyList<string> Reached, IReadOnlyList<string> Missing, double Farthest, string Note);

    public sealed record FactoryPlan(double Range, double Height, IReadOnlyList<FactoryProduct> Products);

    /// <summary>One platform of the warehouse as a place for a crafter: its index in the warehouse plan and its centre.</summary>
    public sealed record FactorySpot(int Index, double X, double Z);

    /// <summary>
    /// Where one product's crafter could stand. <paramref name="Full"/> lists every platform from which all ingredient chests are in range, nearest-farthest first;
    /// <paramref name="Best"/> is the platform that reaches the most ingredients (with <paramref name="Reached"/> and <paramref name="Missing"/>) when none reaches all.
    /// </summary>
    public sealed record ProductReach(string Product, FactoryMode Mode, IReadOnlyDictionary<string, int> Ingredients, IReadOnlyList<(FactorySpot Spot, double Farthest)> Full,
        FactorySpot? Best, IReadOnlyList<string> Reached, IReadOnlyList<string> Missing, double BestFarthest, string Note);

    /// <summary>
    /// The Cheats page's Factory, read-only part. For each wanted product it looks up the recipe and asks from which warehouse platforms an autocrafter, standing
    /// <c>height</c> metres above the warehouse foundations, has every ingredient chest inside its range (the game measures range as a 3D sphere). Where no
    /// platform reaches everything, it records the platform that reaches the most and the ingredients left over, for a local chest to demand later.
    /// </summary>
    public static class FactoryPlanner
    {
        /// <summary>How far above a chest's pivot its body reaches on average, so the sphere test aims at the middle of the chest, not its base.</summary>
        public const double ChestMiddle = 1.0;

        private static readonly Regex RecordPattern = new(@"\{(?:[^{}""]|""(?:[^""\\]|\\.)*"")*\}", RegexOptions.Compiled);

        /// <summary>The labelled chests standing on a warehouse: every Container whose item filter is one item, within the box around the given platforms.</summary>
        public static IReadOnlyList<FactoryChest> WarehouseChests(string saveText, IEnumerable<(double X, double Z)> platforms, double spacing, double platformY)
        {
            var list = platforms.ToList();
            if (list.Count == 0)
                return Array.Empty<FactoryChest>();

            double half = spacing / 2 + 0.3;
            double minX = list.Min(p => p.X) - half, maxX = list.Max(p => p.X) + half, minZ = list.Min(p => p.Z) - half, maxZ = list.Max(p => p.Z) + half;
            var chests = new List<FactoryChest>();

            foreach (Match m in RecordPattern.Matches(saveText))
            {
                var raw = m.Value;
                if (!raw.Contains("\"gId\":\"Container", StringComparison.Ordinal))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    var pos = root.TryGetProperty("pos", out var p) ? p.GetString()?.Split(',') : null;
                    if (pos is null || pos.Length != 3
                        || !double.TryParse(pos[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                        || !double.TryParse(pos[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                        || !double.TryParse(pos[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                        continue;

                    if (x < minX || x > maxX || z < minZ || z > maxZ || Math.Abs(y - platformY - BaseBuildingEngine.OnFoundation) > 0.8)
                        continue;

                    var item = root.TryGetProperty("liGrps", out var g) ? g.GetString() : null;
                    chests.Add(new FactoryChest(root.GetProperty("id").GetInt64(), string.IsNullOrWhiteSpace(item) || item.Contains(',') ? null : item, x, y, z));
                }
                catch (JsonException)
                {
                    // Not a record we can read; leave it be.
                }
            }

            return chests;
        }

        /// <param name="platforms">The warehouse platforms: the crafter may stand on the centre of any of them.</param>
        /// <param name="platformY">The y of the warehouse foundations.</param>
        /// <param name="height">How far above the warehouse foundations the crafter's platform is (the owner's test: 10).</param>
        /// <param name="margin">Metres kept back from the range, because a chest is not a point.</param>
        public static IReadOnlyList<ProductReach> Evaluate(RecipeBook book, IReadOnlyList<FactoryChest> chests, IReadOnlyList<FactorySpot> platforms, double platformY, double height,
            IEnumerable<string> products, double margin = 0.5)
        {
            var range = book.AutoCrafter?.Range ?? 0;
            var crafterY = platformY + height + BaseBuildingEngine.OnFoundation;
            var byItem = chests.Where(c => c.Item is not null).GroupBy(c => c.Item!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var result = new List<ProductReach>();

            foreach (var product in products)
            {
                if (!book.Recipes.TryGetValue(product, out var recipe) || recipe.Ingredients.Count == 0)
                {
                    result.Add(new ProductReach(product, FactoryMode.NoRecipe, new Dictionary<string, int>(), Array.Empty<(FactorySpot, double)>(), null, Array.Empty<string>(), Array.Empty<string>(), 0,
                        "The game has no recipe for this id."));
                    continue;
                }

                if (!RecipeBook.CraftedByAutocrafter(recipe) && recipe.Kind == "item" && recipe.CraftableIn.Count > 0 && !recipe.CraftableIn.Any(c => c is "CraftStationT1" or "CraftStationT2" or "CraftStationT3" or "CraftDroneT1" or "CraftOvenT1" or "CraftQuartzT1" or "CraftToxicRefinementT1" or "CraftBioLab"))
                {
                    result.Add(new ProductReach(product, FactoryMode.OtherMachine, recipe.Ingredients, Array.Empty<(FactorySpot, double)>(), null, Array.Empty<string>(), Array.Empty<string>(), 0,
                        "Made in " + string.Join(", ", recipe.CraftableIn) + ", which an autocrafter cannot use."));
                    continue;
                }

                var needed = recipe.Ingredients.Keys.ToList();
                var full = new List<(FactorySpot Spot, double Farthest)>();
                FactorySpot? best = null;
                var bestReached = new List<string>();
                double bestFar = double.MaxValue;

                if (range > 0)
                {
                    foreach (var spot in platforms)
                    {
                        var reached = new List<string>();
                        double far = 0;
                        foreach (var ingredient in needed)
                        {
                            if (!byItem.TryGetValue(ingredient, out var holders))
                                continue;

                            // The nearest chest of that item to this spot (a 3D distance to the middle of the chest).
                            var d = holders.Min(c => Math.Sqrt(Math.Pow(c.X - spot.X, 2) + Math.Pow(c.Y + ChestMiddle - crafterY, 2) + Math.Pow(c.Z - spot.Z, 2)));
                            if (d <= range - margin)
                            {
                                reached.Add(ingredient);
                                far = Math.Max(far, d);
                            }
                        }

                        if (reached.Count == needed.Count)
                            full.Add((spot, far));

                        // More ingredients reached is better; among equals, the smaller farthest distance.
                        if (reached.Count > bestReached.Count || (reached.Count == bestReached.Count && reached.Count > 0 && Math.Round(far, 2) < Math.Round(bestFar, 2)))
                        {
                            best = spot;
                            bestReached = reached;
                            bestFar = far;
                        }
                    }
                }

                var missing = needed.Where(i => !bestReached.Contains(i)).ToList();
                full = full.OrderBy(f => Math.Round(f.Farthest, 2)).ThenBy(f => f.Spot.Index).ToList();

                if (full.Count > 0)
                    result.Add(new ProductReach(product, FactoryMode.InRange, recipe.Ingredients, full, full[0].Spot, needed, Array.Empty<string>(), full[0].Farthest, "All ingredient chests are inside its range."));
                else
                    result.Add(new ProductReach(product, FactoryMode.LocalChest, recipe.Ingredients, full, best, bestReached, missing, bestReached.Count == 0 ? 0 : bestFar,
                        (missing.Any(m => !byItem.ContainsKey(m)) ? "Not stocked in the warehouse: " + string.Join(", ", missing.Where(m => !byItem.ContainsKey(m))) + ". " : "")
                        + "Out of reach from any platform: " + string.Join(", ", missing) + "."));
            }

            return result;
        }

        /// <summary>The read-only report: for each product, the best single platform for its crafter (see <see cref="Evaluate"/>).</summary>
        public static FactoryPlan Plan(RecipeBook book, IReadOnlyList<FactoryChest> chests, IReadOnlyList<FactorySpot> platforms, double platformY, double height,
            IEnumerable<string> products, double margin = 0.5)
        {
            var crafterY = platformY + height + BaseBuildingEngine.OnFoundation;
            var reaches = Evaluate(book, chests, platforms, platformY, height, products, margin);

            return new FactoryPlan(book.AutoCrafter?.Range ?? 0, height, reaches.Select(r => new FactoryProduct(r.Product, r.Mode, r.Ingredients,
                r.Best is { } b ? (b.X, crafterY, b.Z) : null, r.Reached, r.Missing, r.BestFarthest, r.Note)).ToList());
        }
    }
}
