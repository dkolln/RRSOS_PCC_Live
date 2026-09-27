using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>What the Factory tab shows for one save and one warehouse beacon: the plan, how far each buildable product can be reached, the crafters already standing, and what a removal would take.</summary>
    public sealed record FactoryScreen(
        BuildBeacon Beacon, BuildPlan Warehouse, FactoryBuildPlan Plan, IReadOnlyList<ProductReach> Reaches, IReadOnlyList<FactoryCrafter> Crafters,
        BaseBuildingEngine.RemovalPlan Removal, IReadOnlyList<string> Problems);

    /// <summary>
    /// The Cheats page's Factory: autocrafters on a floor above the warehouse, one per platform, each set to make one product from the warehouse chests in its range
    /// (see <see cref="FactoryPlanner"/> and <see cref="BaseBuildingEngine.PlanFactory"/>). Reads <c>recipes.json</c> (plugin 0.9.0). Building, removing and switching
    /// crafters on or off edit the save with a backup first, like the rest of the Cheats page.
    /// </summary>
    public sealed class FactoryService
    {
        /// <summary>How far above the warehouse foundations the factory floor is: the owner's test (2026-09-27) showed a crafter that high reaches the chests below.</summary>
        public const double Height = 10;

        /// <summary>What one crafter draws (AutoCrafter1, from the item table) when the table has no figure.</summary>
        public const double DefaultCrafterKw = 155;

        // The products the owner named when the tab was designed (2026-09-27), used as the first ticked list. Honey has no recipe and there is no doughnut in this game.
        public static readonly string[] OwnersList =
        {
            "AnimalFood1", "AnimalFood2", "AnimalFood3", "CircuitBoard1", "RocketReactor", "RocketReactor2", "Alloy", "PulsarQuartz", "SolarQuartz", "FabricBlue", "SmartFabric",
            "Rod-uranium", "Rod-iridium", "Rod-alloy", "Rod-osmium", "astrofood2", "CookStew1", "CookStewFish1", "Bacteria1", "Bioplastic1", "FusionEnergyCell", "Fertilizer1", "Fertilizer2",
            "RedPowder1", "Mutagen1", "Mutagen2", "Mutagen3", "Mutagen4", "LarvaeBase1", "LarvaeBase3", "Drone1", "Drone2", "CookChocolate", "CookFlour", "CookCroissant", "CookCookie1",
            "Flare", "Explosive"
        };

        private readonly BaseBuildingService _building;
        private readonly SaveResupplyService _saves;
        private readonly ItemCatalog _catalog;
        private readonly IConfiguration _config;

        public FactoryService(BaseBuildingService building, SaveResupplyService saves, ItemCatalog catalog, IConfiguration config)
        {
            _building = building;
            _saves = saves;
            _catalog = catalog;
            _config = config;
        }

        public static string BookPath(IConfiguration config) => Path.Combine(LivePaths.Folder(config), "recipes.json");

        /// <summary>The game's recipes as the plugin wrote them, or null when the file is not there yet (start the game once with plugin 0.9.0 or later).</summary>
        public RecipeBook? Book()
        {
            try
            {
                var path = BookPath(_config);
                if (!File.Exists(path))
                    return null;

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return RecipeBook.Parse(reader.ReadToEnd());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>What a crafter draws: from the item table when it lists one, else the known 155 kW.</summary>
        public double CrafterKw => DefaultCrafterKw;

        /// <summary>The name a sign shows for a product: the item's name, unless that is missing or shared with another product, then its id.</summary>
        public Func<string, string> LabelOf(IEnumerable<string> products)
        {
            var list = products.Distinct(StringComparer.Ordinal).ToList();
            var names = list.ToDictionary(p => p, p => _catalog.NameOf(p), StringComparer.Ordinal);
            var shared = names.GroupBy(n => n.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return product =>
            {
                var name = names.TryGetValue(product, out var n) ? n : _catalog.NameOf(product);
                return string.IsNullOrWhiteSpace(name) || string.Equals(name, product, StringComparison.OrdinalIgnoreCase) || shared.Contains(name) ? product : name.Replace("\"", "");
            };
        }

        public string NameOf(string id) => _catalog.NameOf(id);

        /// <summary>The beacons that ask for the warehouse (named All, Everything or Warehouse).</summary>
        public async Task<IReadOnlyList<BuildBeacon>> WarehouseBeaconsAsync(string savePath)
        {
            var (beacons, _) = await _building.ScanAsync(savePath);
            return beacons.Where(b => _building.RecipeFor(b.Text)?.Key == "warehouse").ToList();
        }

        public Task<FactoryScreen?> LoadAsync(string savePath, long beaconId, BuildTemplate template, RecipeBook book, IReadOnlyCollection<string> products, bool fullFloor) => Task.Run(() =>
        {
            var text = BaseBuildingService.ReadText(savePath, out _);
            return text is null ? null : Load(text, beaconId, template, book, products, fullFloor);
        });

        public FactoryScreen Load(string text, long beaconId, BuildTemplate template, RecipeBook book, IReadOnlyCollection<string> products, bool fullFloor)
        {
            var warehouse = _building.WarehousePlan(text, beaconId, template);
            var problems = new List<string>(warehouse.Problems);
            var empty = new FactoryBuildPlan(warehouse.Beacon, book.AutoCrafter?.Range ?? 0, Height, 0, Array.Empty<FactoryFloorCell>(), Array.Empty<FactoryProduct>(), problems, 0);

            if (problems.Count > 0)
                return new FactoryScreen(warehouse.Beacon, warehouse, empty, Array.Empty<ProductReach>(), Array.Empty<FactoryCrafter>(), new BaseBuildingEngine.RemovalPlan(0, 0, 0, 0, Array.Empty<string>(), Array.Empty<long>(), Array.Empty<long>()), problems);

            var plan = BaseBuildingEngine.PlanFactory(text, beaconId, template, warehouse.Platforms, book, products, Height, fullFloor, CrafterKw, LabelOf(products), _building.IsBuilding);

            // How far every buildable product can be reached, for the checklist.
            var spots = warehouse.Platforms.Select(p => new FactorySpot(p.Index, p.X, p.Z)).ToList();
            var chests = FactoryPlanner.WarehouseChests(text, spots.Select(s => (s.X, s.Z)), template.Spacing, warehouse.Beacon.FoundationY);
            var reaches = FactoryPlanner.Evaluate(book, chests, spots, warehouse.Beacon.FoundationY, Height, book.Buildable().Select(r => r.Id));

            var footprint = BaseBuildingEngine.FactoryFootprint(warehouse.Beacon, template, warehouse.Platforms, Height);
            var crafters = BaseBuildingEngine.ListCrafters(text, footprint);
            var removal = BaseBuildingEngine.PlanRemoval(text, footprint, _building.IsBuilding, includeCrafters: true);

            return new FactoryScreen(warehouse.Beacon, warehouse, plan, reaches, crafters, removal, problems);
        }

        /// <summary>Builds the factory into the save, with a backup first (the game must be at its main menu). The plan is worked out again from the file as it is now.</summary>
        public Task<SaveEdit<BuildOutcome>> BuildAsync(string savePath, long beaconId, BuildTemplate template, RecipeBook book, IReadOnlyCollection<string> products, bool fullFloor, bool on) =>
            _saves.EditAsync<BuildOutcome>(savePath, text =>
            {
                var warehouse = _building.WarehousePlan(text, beaconId, template);
                if (warehouse.Problems.Count > 0)
                    return ((string?)null, new BuildOutcome(null, 0, 0, 0, 0, warehouse.Problems), (string?)("Nothing was built. " + string.Join(" ", warehouse.Problems)));

                var plan = BaseBuildingEngine.PlanFactory(text, beaconId, template, warehouse.Platforms, book, products, Height, fullFloor, CrafterKw, LabelOf(products), _building.IsBuilding);
                var outcome = BaseBuildingEngine.ApplyFactory(text, plan, on);
                if (outcome.Failed)
                    return ((string?)null, outcome, (string?)("Nothing was built. " + string.Join(" ", outcome.Problems)));

                // The two ramps are part of the factory (the owner: "I would expect ramps to automatically just be a part of said factory"), laid right after the floor. A ramp
                // that is already exactly right, or whose platform is not there for some reason, is skipped quietly; it never fails the rest of the build.
                var working = outcome.NewText!;
                var extraTiles = 0;
                foreach (var ramp in new[] { BaseBuildingEngine.EastRamp, BaseBuildingEngine.BackRamp })
                {
                    var wh = _building.WarehousePlan(working, beaconId, template);
                    if (wh.Problems.Count > 0)
                        continue;

                    var rampOutcome = BaseBuildingEngine.ApplyRamp(working, wh, Height, ramp);
                    if (!rampOutcome.Failed)
                    {
                        working = rampOutcome.NewText!;
                        extraTiles += rampOutcome.Added;
                    }
                }

                var final = outcome with { NewText = working, Foundations = outcome.Foundations + extraTiles };
                return (working, final, (string?)null);
            }, "building the factory");

        /// <summary>Removes the factory: its platforms, crafters, signs, the chests on the floor and everything in them (see <see cref="BaseBuildingEngine.Remove"/>).</summary>
        public Task<SaveEdit<BaseBuildingEngine.RemovalOutcome>> RemoveAsync(string savePath, long beaconId, BuildTemplate template) =>
            _saves.EditAsync<BaseBuildingEngine.RemovalOutcome>(savePath, text =>
            {
                var warehouse = _building.WarehousePlan(text, beaconId, template);
                var footprint = BaseBuildingEngine.FactoryFootprint(warehouse.Beacon, template, warehouse.Platforms, Height);
                var outcome = BaseBuildingEngine.Remove(text, warehouse.Problems.Count > 0 ? footprint with { Problems = warehouse.Problems } : footprint, _building.IsBuilding, includeCrafters: true);
                return outcome.Failed ? ((string?)null, outcome, (string?)("Nothing was removed. " + string.Join(" ", outcome.Problems))) : (outcome.NewText, outcome, (string?)null);
            }, "removing the factory");

        /// <summary>Switches every crafter of the factory (or one, by id) on or off, by writing only its recipe.</summary>
        /// <summary>
        /// Adds one crafter for <paramref name="product"/>, ad hoc, to the first free platform (nearest, farthest-ingredient-first) that reaches all its ingredients. The floor there
        /// must already stand (build the factory first). Refused when nothing reaches it, or every platform that does already has a crafter.
        /// </summary>
        public Task<SaveEdit<BuildOutcome>> AddCrafterAsync(string savePath, long beaconId, BuildTemplate template, RecipeBook book, string product, bool on) =>
            _saves.EditAsync<BuildOutcome>(savePath, text =>
            {
                var warehouse = _building.WarehousePlan(text, beaconId, template);
                if (warehouse.Problems.Count > 0)
                    return ((string?)null, new BuildOutcome(null, 0, 0, 0, 0, warehouse.Problems), (string?)("Nothing was added. " + string.Join(" ", warehouse.Problems)));

                var footprint = BaseBuildingEngine.FactoryFootprint(warehouse.Beacon, template, warehouse.Platforms, Height);
                var spots = warehouse.Platforms.Select(p => new FactorySpot(p.Index, p.X, p.Z)).ToList();
                var chests = FactoryPlanner.WarehouseChests(text, spots.Select(s => (s.X, s.Z)), template.Spacing, warehouse.Beacon.FoundationY);
                var reach = FactoryPlanner.Evaluate(book, chests, spots, warehouse.Beacon.FoundationY, Height, new[] { product }).FirstOrDefault();
                var label = LabelOf(new[] { product })(product);

                if (reach is null || reach.Mode != FactoryMode.InRange || reach.Full.Count == 0)
                {
                    var problem = "No platform reaches every ingredient of this product.";
                    return ((string?)null, new BuildOutcome(null, 0, 0, 0, 0, new[] { problem }), (string?)problem);
                }

                BuildOutcome? last = null;
                foreach (var (spot, _) in reach.Full)
                {
                    last = BaseBuildingEngine.AddCrafter(text, footprint, spot.Index, product, label, on);
                    if (!last.Failed)
                        return (last.NewText, last, (string?)null);
                }

                var msg = "Every platform that reaches it already has a crafter.";
                return ((string?)null, last ?? new BuildOutcome(null, 0, 0, 0, 0, new[] { msg }), (string?)msg);
            }, "adding a crafter");

        /// <summary>Removes one crafter and its sign, leaving the platform's foundation and everything else untouched.</summary>
        public Task<SaveEdit<CrafterEditOutcome>> RemoveCrafterAsync(string savePath, long crafterId) =>
            _saves.EditAsync<CrafterEditOutcome>(savePath, text =>
            {
                var outcome = BaseBuildingEngine.RemoveCrafter(text, crafterId);
                return outcome.Failed ? ((string?)null, outcome, (string?)string.Join(" ", outcome.Problems)) : (outcome.NewText, outcome, (string?)null);
            }, "removing a crafter");

        /// <summary>
        /// Builds (or finishes) one of the two ramps the owner asked for, over the factory floor: <paramref name="east"/> true for the one along the row nearest the beacon,
        /// false for the one down the back column. The warehouse itself is never touched. Running it again after a partial edit finishes the job.
        /// </summary>
        public Task<SaveEdit<RampOutcome>> BuildRampAsync(string savePath, long beaconId, BuildTemplate template, bool east) =>
            _saves.EditAsync<RampOutcome>(savePath, text =>
            {
                var warehouse = _building.WarehousePlan(text, beaconId, template);
                var outcome = BaseBuildingEngine.ApplyRamp(text, warehouse, Height, east ? BaseBuildingEngine.EastRamp : BaseBuildingEngine.BackRamp);
                return outcome.Failed ? ((string?)null, outcome, (string?)string.Join(" ", outcome.Problems)) : (outcome.NewText, outcome, (string?)null);
            }, east ? "building the east ramp" : "building the back ramp");

        public Task<SaveEdit<FactoryToggleOutcome>> SetPowerAsync(string savePath, long beaconId, BuildTemplate template, RecipeBook book, bool on, long? onlyId = null) =>
            _saves.EditAsync<FactoryToggleOutcome>(savePath, text =>
            {
                var warehouse = _building.WarehousePlan(text, beaconId, template);
                var footprint = BaseBuildingEngine.FactoryFootprint(warehouse.Beacon, template, warehouse.Platforms, Height);
                var outcome = BaseBuildingEngine.SetCrafters(text, footprint, on, id => book.Recipes.ContainsKey(id), onlyId);
                return outcome.Failed ? ((string?)null, outcome, (string?)string.Join(" ", outcome.Problems)) : (outcome.NewText, outcome, (string?)null);
            }, on ? "switching the crafters on" : "switching the crafters off");
    }
}
