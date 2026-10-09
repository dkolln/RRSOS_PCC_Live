using System.Text;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>A family of items a row of chests is labelled with, in order: "Fish" is Fish1Eggs, Fish2Eggs, ...</summary>
    public sealed record BuildRecipe(string Key, string Name, IReadOnlyList<string> Items, IReadOnlyList<BuildBundle>? Bundles = null, bool Warehouse = false, int WarehouseSlots = 0, int WarehouseChests = 0)
    {
        /// <summary>How many chests the group is: one per item, or one per bundle for a "one of each" group.</summary>
        public int ChestCount => Warehouse ? WarehouseChests : Bundles?.Count ?? Items.Count;

        /// <summary>
        /// The most a stocked chest must hold, so which templates are big enough (0 for an ordinary group). A bundle that can be split over several chests does
        /// not count here (nor one that drops what the save has already unlocked).
        /// </summary>
        public int MinSlots => Warehouse ? WarehouseSlots : Bundles is { Count: > 0 } ? Bundles.Where(b => !b.SkipUnlocked && !b.Split).Select(b => b.Items.Count).DefaultIfEmpty(0).Max() : 0;
    }

    /// <summary>
    /// Reads saves for the Cheats page's Base Building (beacons, template capture, row plans), read-only. See
    /// <see cref="BaseBuildingEngine"/>. The recipes come from the item table, so a new fish egg in <c>worldobjectdata.json</c>
    /// is in the next fish row.
    /// </summary>
    public sealed class BaseBuildingService
    {
        // Each recipe: the beacon words that pick it, and how its items are found and ordered in the item table.
        private static readonly (string Key, string Name, string[] Words, Regex Pattern, string[] Extra)[] Families =
        {
            ("fish", "Fish eggs", new[] { "fish" }, new Regex(@"^Fish(\d+)Eggs$", RegexOptions.Compiled), Array.Empty<string>()),
            ("frog", "Frog eggs", new[] { "frog", "frogs" }, new Regex(@"^Frog(\d+)Eggs$", RegexOptions.Compiled), new[] { "FrogGoldEggs" }),
            ("butterfly", "Butterfly larvae", new[] { "butterfly", "butterflies" }, new Regex(@"^Butterfly(\d+)Larvae$", RegexOptions.Compiled), Array.Empty<string>()),
            ("tree", "Tree seeds", new[] { "tree", "trees" }, new Regex(@"^(?:TreeRoot|Tree(\d+)Seed)$", RegexOptions.Compiled), Array.Empty<string>()),
            // Seed0 to Seed6, then the Humble ones in number order (Seed7Humble, Seed8Humble, ...), then the golden seed.
            ("seed", "Plant seeds", new[] { "seed", "seeds", "plant", "plants" }, new Regex(@"^Seed(\d+)(Humble)?$", RegexOptions.Compiled), new[] { "SeedGold" }),
            // Each harvested crop with its seed beside it (crop on the left, seed on the right): Eggplant, Squash, Beans, Mushroom, then Cocoa and Wheat.
            ("vegetable", "Vegetables", new[] { "vegetable", "vegetables", "veg", "veggie", "veggies", "crop", "crops" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "Vegetable0Growable", "Vegetable0Seed", "Vegetable1Growable", "Vegetable1Seed", "Vegetable2Growable", "Vegetable2Seed",
                        "Vegetable3Growable", "Vegetable3Seed", "CookCocoaGrowable", "CookCocoaSeed", "CookWheatGrowable", "CookWheatSeed" }),
            // The common, uncommon and rare larvae (LarvaeBase1 to 3), then the bee larva and the silk worm. Butterfly larvae are their own recipe.
            ("larvae", "Larvae", new[] { "larva", "larvae" }, new Regex(@"^LarvaeBase(\d+)$", RegexOptions.Compiled), new[] { "Bee1Larvae", "SilkWorm" }),
            // The petri dishes: Mutagen1 to Mutagen4, the bacteria sample, then the DNA sequencer and the genetic trait (the owner keeps genetics with them).
            ("petri", "Petri dishes", new[] { "petri", "dish", "dishes", "mutagen", "mutagens", "bacteria", "dna", "genetic", "genetics" }, new Regex(@"^Mutagen(\d+)$", RegexOptions.Compiled), new[] { "Bacteria1", "DNASequence", "GeneticTrait" }),
            // The quartz crystals in the game's own order (as in its supply lists): Pulsar, Balzar, Magnetar, Quasar, Solar, Cosmic.
            ("quartz", "Quartz crystals", new[] { "quartz", "crystal", "crystals" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "PulsarQuartz", "BalzarQuartz", "MagnetarQuartz", "QuasarQuartz", "SolarQuartz", "CosmicQuartz" }),
            // Each material with its rod beside it, so each pair lands as the left and right chest of one position on a platform:
            // Iridium and Iridium Rod, Uranium and Uranium Rod, Alloy and Alloy Rod, Osmium and Osmium Rod, then the game's two newer
            // pairs (plastic, tungsten), which appear once the item table names them.
            ("rods", "Rods (material + rod)", new[] { "rod", "rods" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "Iridium", "Rod-iridium", "Uranim", "Rod-uranium", "Alloy", "Rod-alloy", "Osmium", "Rod-osmium",
                        "PlasticPolymer", "Rod-plastic", "Minable-Tungsten", "Rod-tungsten" }),
            // The plain ores, in the owner's order, then the other planets' ores in the game's order (Bauxite to Amber), then ice (called an ore here on purpose). Iridium, Uranium and Osmium are with their rods in the Rods recipe.
            ("ore", "Ores", new[] { "ore", "ores", "mineral", "minerals" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "Iron", "Silicon", "Titanium", "Magnesium", "Cobalt", "Aluminium", "Sulfur", "Zeolite", "Obsidian",
                        "Bauxite", "Dolomite", "Uraninite", "Selenium", "Phosphorus", "Amber", "ice" }),
            // The things machines and crafting consume, in the owner's order: gas capsules, bio nugget, algae, the three plankton, fertilizers, explosive powder,
            // fusion cell, the fabrics and silk, circuit board, rocket engine, then the access cards, the explosive, the flare and the animal bones (the owner keeps them with the rocket engine). Fertilizer3 and RocketReactor2 are the game's next tiers, in the list until the item table names them.
            ("materials", "Crafting materials", new[] { "crafting", "material", "materials" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "NitrogenCapsule1", "MethanCapsule1", "Bioplastic1", "Algae1Seed", "Phytoplankton1", "Phytoplankton2", "Phytoplankton3",
                        "Fertilizer1", "Fertilizer2", "Fertilizer3", "RedPowder1", "FusionEnergyCell", "FabricBlue", "SmartFabric", "Silk", "CircuitBoard1",
                        "RocketReactor", "RocketReactor2", "Keycard1", "KeyCard2", "Explosive", "Flare", "AnimalBones" }),
            // The fuses, in the game's own order: the cartridge, then Pressure, Heat, Energy, Plants, Oxygen, Production, and the newer ones once the item table names them.
            ("fuse", "Fuses", new[] { "fuse", "fuses" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "FuseCartridge", "FusePressure1", "FuseHeat1", "FuseEnergy1", "FusePlants1", "FuseOxygen1", "FuseProduction1",
                        "FuseTradeRocketsSpeed1", "FuseAnimals1", "FuseGrowth1", "FuseInsects1", "FusePurification1" }),
            // Prepared food and cooking (the harvested crops are in Vegetables; astrofood is in Essentials): flour, chocolate, cookie, honey, animal food, and the
            // dishes from later worlds once they are named.
            ("food", "Food and cooking", new[] { "food", "foods", "cooking", "cook", "kitchen" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "CookFlour", "CookChocolate", "CookCookie1", "honey", "AnimalFood1", "AnimalFood2", "AnimalFood3",
                        "CookCroissant", "CookCake1", "CookStew1", "CookStewFish1" }),
            // The toxic and purification group in the owner's order: what is toxic, then what cleans it, then the explosives and medicine.
            ("toxic", "Toxic and purification", new[] { "toxic", "toxin", "toxins", "purification", "purify" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "ToxicGoo", "ToxicWater", "Toxins", "ToxicSpores", "MicroPlastics",
                        "PurifiedWater", "PurificationCapsule", "PurificationGel", "ChlorineCapsule1", "PristineMushroom",
                        "AntiToxinsExplosive1", "AntiToxinsExplosive2", "ToxicityMedecine", "ToxicityAmmo", "ToxicityAmmoPack", "ToxicityMedecinePack" }),
            // The drones.
            ("drone", "Drones", new[] { "drone", "drones" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "Drone1", "Drone2", "Drone3" }),
            // What the owner keeps together to stay alive: the two space foods, water and oxygen.
            ("essentials", "Essentials", new[] { "essential", "essentials", "survival" }, new Regex(@"(?!)", RegexOptions.Compiled),
                new[] { "astrofood", "astrofood2", "WaterBottle1", "OxygenCapsule1" })
        };

        private readonly ItemCatalog _catalog;
        private readonly SaveResupplyService _saves;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public BaseBuildingService(ItemCatalog catalog, SaveResupplyService saves, IConfiguration config, IWebHostEnvironment env)
        {
            _catalog = catalog;
            _saves = saves;
            _config = config;
            _env = env;
        }

        /// <summary>
        /// The buildings the game locks behind blueprints and the group id of the blueprint chip, from <c>blueprints.json</c>, which the plugin (0.8.0 and up)
        /// writes the first time a world is loaded; null when it has not been written yet. Messages-only unlocks are not blueprints and are left out.
        /// </summary>
        public (string Chip, IReadOnlyList<string> Groups)? BlueprintGroups()
        {
            try
            {
                var path = Path.Combine(LivePaths.Folder(_config), "blueprints.json");
                if (!File.Exists(path))
                    return null;

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = System.Text.Json.JsonDocument.Parse(stream);
                var chip = doc.RootElement.TryGetProperty("chip", out var c) && c.GetString() is { Length: > 0 } id ? id : "BlueprintT1";
                // The buildings behind blueprints (one list per tier), then the ones found by deconstructing them (the exercise bike, ...). Message-only unlocks are not blueprints.
                var groups = doc.RootElement.GetProperty("tiers").EnumerateArray()
                    .SelectMany(tier => tier.EnumerateArray().Select(g => g.GetString() ?? ""))
                    .Concat(doc.RootElement.TryGetProperty("loot", out var loot) ? loot.EnumerateArray().Select(g => g.GetString() ?? "") : Enumerable.Empty<string>())
                    .Where(g => g.Length > 0).Distinct(StringComparer.Ordinal).ToList();

                return groups.Count == 0 ? null : (chip, groups);
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or InvalidOperationException)
            {
                return null;
            }
        }

        // Something a new platform must not be built on top of: any building piece, machine or container.
        public bool IsBuilding(string gId) => _catalog.CategoryOf(gId) is ItemCategory.Machine or ItemCategory.BasePart or ItemCategory.Container
            or ItemCategory.WorldMarker or ItemCategory.Wreck;

        // The warehouse: the groups in the rows the owner chose (2026-09-26), each group on platforms of its own, every row 10 platforms long with today's item table,
        // with an aisle of blank platforms on each side (2026-09-27). The signs are the owner's own index from Custom-2, in his words, top to bottom on the first
        // platform of each row: "<>" is a group on both the left and right chests, "<" on the left ones only, ">" on the right ones only.
        // A recipe that is not named here (a new family) goes in a row of its own after these.
        private static BaseBuildingEngine.WarehouseSign L(string text) => new(false, text);
        private static BaseBuildingEngine.WarehouseSign R(string text) => new(true, text);

        private static readonly (string[] Keys, BaseBuildingEngine.WarehouseSign[] Signs)[] WarehouseLayout =
        {
            (new[] { "butterfly", "larvae", "petri" }, new[] { L("Butterfly <>"), L("Larvae <>"), L("Petri <>") }),
            (new[] { "frog", "tree" }, new[] { L("Frog Egg <>"), L("Tree Seed <>") }),
            (new[] { "seed", "vegetable", "food" }, new[] { L("Plant Seed <>"), L("Vegetable <>"), L("Vegetable Seed <>"), R("Cook <>"), R("Animal Food <>"), R("Honey >") }),
            (new[] { "ore", "rods", "fuse" }, new[] { L("Ore <>"), L("Fuse <>"), L("Rods >") }),
            (new[] { "materials", "quartz", "drone", "essentials" }, new[] { L("Nitrogen Methane"), L("BioNugget Plankton"), L("Fertilizer Boom Pwdr"), L("Fabric RocketEngine"),
                                                                              R("Key Cards Explosives"), R("Bones Quartz"), R("Algae EnergyCell"), R("Circuit Board Flare") }),
            (new[] { "gear", "fish", "toxic" }, new[] { L("< Space Suit"), L("< Vehicle Gear"), L("< Money"), R("Personal Gear >"), R("Blueprint >"), R("Fish Egg <>") })
        };

        // Chests of the warehouse that hold nothing yet: labelled with a plain title, no item filter, so Resupply leaves them alone (the owner's holding tanks).
        private static readonly Dictionary<string, string> HoldingTanks = new(StringComparer.Ordinal) { ["DNASequence"] = "DNA", ["GeneticTrait"] = "Genetics" };

        // The warehouse is built from Container3 only (80 slots).
        private const int WarehouseMinSlots = 80;

        // 11 platforms deep (2026-09-27, the owner): one extra bare platform at the back of every row, beyond whatever the groups need (which was 10).
        public const int WarehouseDepth = 11;

        private static readonly string[] WarehouseWords = { "all", "everything", "warehouse" };

        /// <summary>The recipes of one group each, then the whole warehouse as one more (built from a beacon called "All").</summary>
        public IReadOnlyList<BuildRecipe> Recipes()
        {
            var single = SingleRecipes();
            if (single.Count == 0)
                return single;

            var gear = single.FirstOrDefault(r => r.Key == "gear");
            return single.Append(new BuildRecipe("warehouse", "Everything (warehouse)", Array.Empty<string>(), null, true, Math.Max(gear?.MinSlots ?? 0, WarehouseMinSlots), single.Sum(r => r.ChestCount))).ToList();
        }

        /// <summary>The warehouse's rows, worked out from the single recipes: an aisle of blank platforms, the six rows with their signs, then another aisle.</summary>
        public IReadOnlyList<BaseBuildingEngine.WarehouseRow> WarehouseRows()
        {
            var single = SingleRecipes();
            BaseBuildingEngine.WarehouseGroup Group(BuildRecipe r) => new(r.Name, r.Items, r.Bundles);
            var none = Array.Empty<BaseBuildingEngine.WarehouseSign>();

            var content = WarehouseLayout
                .Select(row => new BaseBuildingEngine.WarehouseRow(
                    row.Keys.Select(k => single.FirstOrDefault(r => r.Key == k)).Where(r => r is not null).Select(r => Group(r!)).ToList(), row.Signs))
                .Where(row => row.Groups.Count > 0)
                .ToList();

            var placed = WarehouseLayout.SelectMany(row => row.Keys).ToHashSet();
            var rest = single.Where(r => !placed.Contains(r.Key)).Select(Group).ToList();
            if (rest.Count > 0)
                content.Add(new BaseBuildingEngine.WarehouseRow(rest, none));

            var aisle = new BaseBuildingEngine.WarehouseRow(Array.Empty<BaseBuildingEngine.WarehouseGroup>(), none, true);
            return new[] { aisle }.Concat(content).Append(aisle).ToList();
        }

        private IReadOnlyList<BuildRecipe> SingleRecipes()
        {
            var known = _catalog.AllProducts.Select(p => p.GId).ToHashSet(StringComparer.Ordinal);

            return Families.Select(f =>
            {
                var items = known
                    .Select(g => (G: g, M: f.Pattern.Match(g)))
                    .Where(t => t.M.Success)
                    .OrderBy(t => t.M.Groups[1].Success ? int.Parse(t.M.Groups[1].Value) : -1) // an id without a number (Tree Bark) comes first
                    .Select(t => t.G)
                    .Concat(f.Extra.Where(known.Contains))
                    .Select(g => HoldingTanks.TryGetValue(g, out var tank) ? "=" + tank : g)
                    .ToList();

                return new BuildRecipe(f.Key, f.Name, items);
            }).Where(r => r.Items.Count > 0).Append(GearRecipe(known)).Where(r => r.ChestCount > 0).ToList();
        }

        private static readonly string[] GearWords = { "gear", "equipment", "suit", "suits", "spacesuit", "spacesuits", "blueprint", "blueprints", "token", "tokens", "loadout" };

        // The stocked group: spacesuits, personal equipment and vehicle equipment go one of each into their own chest; the blueprint chest is filled with blueprint chips
        // (each unlocks one more building) and the terra token box with 5,000 tokens. Read from the item table by type, so a new suit or upgrade joins by itself.
        private BuildRecipe GearRecipe(HashSet<string> known)
        {
            IReadOnlyList<string> Of(Func<string, bool> pick) => known.Where(pick).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
            int Number(string g) => int.TryParse(Regex.Match(g, @"\d+").Value, out var n) ? n : 0;

            var suits = known.Where(g => _catalog.TypeOf(g) == ItemType.Spacesuit).OrderBy(Number).ToList();
            var personal = Of(g => _catalog.TypeOf(g) == ItemType.Tool).Concat(Of(g => _catalog.TypeOf(g) == ItemType.Gear && !g.StartsWith("BlueprintT", StringComparison.Ordinal))).ToList();
            var vehicle = Of(g => _catalog.TypeOf(g) == ItemType.Part);

            var bundles = new List<BuildBundle>();
            if (suits.Count > 0) bundles.Add(new BuildBundle("Spacesuits", suits));
            if (personal.Count > 0) bundles.Add(new BuildBundle("Personal equipment", personal));
            if (vehicle.Count > 0) bundles.Add(new BuildBundle("Vehicle equipment", vehicle));
            // Blueprints are chips, and every one in a real save is BlueprintT1 (never T2 or T3, which the game drops on load). A chip linked to a building
            // unlocks exactly that one when claimed, so with the plugin's list of the buildings behind blueprints the chest gets one linked chip for each
            // building the save has not unlocked yet. Without the list, it is filled with bare chips (each unlocks the next locked building).
            if (BlueprintGroups() is { } blueprints)
                bundles.Add(new BuildBundle("Blueprints", blueprints.Groups.Select(g => blueprints.Chip + "@" + g).ToList(), null, SkipUnlocked: true, Split: true));
            else if (known.Contains("BlueprintT1"))
                bundles.Add(new BuildBundle("Blueprints", Array.Empty<string>(), "BlueprintT1"));
            if (known.Contains("TerraTokens5000")) bundles.Add(new BuildBundle("Terra tokens", Array.Empty<string>(), "TerraTokens5000"));

            return new BuildRecipe("gear", "Equipment and tokens (one of each)", bundles.SelectMany(b => b.Items).ToList(), bundles);
        }

        /// <summary>The recipe a beacon's name asks for ("Fish", "fish eggs", "Butterflies"), or null.</summary>
        public BuildRecipe? RecipeFor(string beaconText)
        {
            var word = new string(beaconText.Where(char.IsLetter).ToArray()).ToLowerInvariant();

            if (WarehouseWords.Contains(word))
                return Recipes().FirstOrDefault(r => r.Key == "warehouse");

            if (GearWords.Any(w => word == w || word.StartsWith(w, StringComparison.Ordinal)))
                return Recipes().FirstOrDefault(r => r.Key == "gear");

            foreach (var f in Families)
            {
                if (f.Words.Any(w => word == w || word.StartsWith(w, StringComparison.Ordinal)))
                    return Recipes().FirstOrDefault(r => r.Key == f.Key);
            }

            return null;
        }

        internal static string? ReadText(string path, out string? error)
        {
            error = null;

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var bytes = memory.ToArray();
                var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                return Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = "Could not read the save: " + ex.Message;
                return null;
            }
        }

        public Task<(IReadOnlyList<BuildBeacon> Beacons, string? Error)> ScanAsync(string savePath) => Task.Run(() =>
        {
            var text = ReadText(savePath, out var error);
            return text is null
                ? ((IReadOnlyList<BuildBeacon>)Array.Empty<BuildBeacon>(), error)
                : (BaseBuildingEngine.FindBeacons(text), (string?)null);
        });

        public Task<CaptureResult> CaptureAsync(string savePath, long beaconId, string name) => Task.Run(() =>
        {
            var text = ReadText(savePath, out var error);
            return text is null ? new CaptureResult(null, new[] { error! }) : BaseBuildingEngine.Capture(text, beaconId, name);
        });

        public Task<BuildPlan?> PlanAsync(string savePath, long beaconId, BuildTemplate template, BuildRecipe recipe) => Task.Run(() =>
        {
            var text = ReadText(savePath, out _);
            if (text is null)
                return (BuildPlan?)null;

            return MakePlan(text, beaconId, template, recipe);
        });

        public BuildPlan WarehousePlan(string text, long beaconId, BuildTemplate template) =>
            BaseBuildingEngine.PlanWarehouse(text, beaconId, template, WarehouseRows(), IsBuilding, WarehouseDepth);

        /// <summary>What removing the warehouse of this beacon would delete, read-only (null when the save cannot be read).</summary>
        public Task<BaseBuildingEngine.RemovalPlan?> WarehouseScanAsync(string savePath, long beaconId, BuildTemplate template) => Task.Run(() =>
        {
            var text = ReadText(savePath, out _);
            if (text is null)
                return (BaseBuildingEngine.RemovalPlan?)null;

            var plan = WarehousePlan(text, beaconId, template);
            return plan.Problems.Count > 0 ? null : BaseBuildingEngine.PlanRemoval(text, plan, IsBuilding);
        });

        /// <summary>
        /// Removes the warehouse of this beacon from the save: its platforms, chests and the items in them (see <see cref="BaseBuildingEngine.Remove"/>), with a
        /// backup first. The game must be at its main menu.
        /// </summary>
        public Task<SaveEdit<BaseBuildingEngine.RemovalOutcome>> RemoveWarehouseAsync(string savePath, long beaconId, BuildTemplate template) =>
            _saves.EditAsync<BaseBuildingEngine.RemovalOutcome>(savePath, text =>
            {
                var outcome = BaseBuildingEngine.Remove(text, WarehousePlan(text, beaconId, template), IsBuilding);
                return outcome.Failed ? ((string?)null, outcome, (string?)("Nothing was removed. " + string.Join(" ", outcome.Problems))) : (outcome.NewText, outcome, (string?)null);
            }, "removing the warehouse");

        /// <summary>The teleporter site this beacon would get (read-only; null when the save cannot be read).</summary>
        public Task<TeleportPlan?> PlanTeleporterAsync(string savePath, long beaconId, bool faceAway) => Task.Run(() =>
        {
            var text = ReadText(savePath, out _);
            return text is null ? (TeleportPlan?)null : BaseBuildingEngine.PlanTeleporter(text, beaconId, faceAway, IsBuilding);
        });

        /// <summary>
        /// Builds the teleporter site into the save: the plan is worked out again from the file as it is right now, then written with a backup
        /// (see <see cref="SaveResupplyService.EditAsync{T}"/>). The game must be at its main menu.
        /// </summary>
        public Task<SaveEdit<TeleportOutcome>> BuildTeleporterAsync(string savePath, long beaconId, bool faceAway, string? label) =>
            _saves.EditAsync<TeleportOutcome>(savePath, text =>
            {
                var plan = BaseBuildingEngine.PlanTeleporter(text, beaconId, faceAway, IsBuilding);
                var outcome = BaseBuildingEngine.ApplyTeleporter(text, plan, label);
                return outcome.Failed ? ((string?)null, outcome, (string?)("Nothing was built. " + string.Join(" ", outcome.Problems))) : (outcome.NewText, outcome, (string?)null);
            }, "building the teleporter");

        /// <summary>One base the Main Base page can build: a tier, with the captured template that holds it. Every tier is built in the same frame (the anchor foundation is the origin), so a lower tier is found already there when a higher one is built over it.</summary>
        public sealed record MainBaseTier(string Key, string Label, string File);

        /// <summary>The tiers, lowest first. Add a tier by capturing it into Assets and listing it here.</summary>
        public static readonly IReadOnlyList<MainBaseTier> MainBaseTiers = new[]
        {
            new MainBaseTier("tier1", "Tier 1 base", "main-base-tier1.json"),
            new MainBaseTier("full", "Full base", "main-base-template.json")
        };

        public const string DefaultMainBaseTier = "tier1";

        private readonly Dictionary<string, (MainBaseTemplate? Template, string? Error)> _mainBases = new();

        /// <summary>A captured Main Base (Assets/main-base-*.json, made by the tools/capture-*.py scripts), read once. Null with an error when it cannot be read.</summary>
        public (MainBaseTemplate? Template, string? Error) MainBase(string? tier = null)
        {
            var entry = MainBaseTiers.FirstOrDefault(t => t.Key == tier) ?? MainBaseTiers.First(t => t.Key == DefaultMainBaseTier);

            lock (_mainBases)
            {
                if (_mainBases.TryGetValue(entry.Key, out var known))
                    return known;

                MainBaseTemplate? template = null;
                string? error = null;

                try
                {
                    var path = Path.Combine(_env.ContentRootPath, "Assets", entry.File);
                    template = System.Text.Json.JsonSerializer.Deserialize<MainBaseTemplate>(File.ReadAllText(path), BaseBuildingEngine.TemplateJson);
                    if (template is null || template.Objects.Count == 0)
                    {
                        template = null;
                        error = $"The {entry.Label} template file is empty.";
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    error = $"Could not read the {entry.Label} template: " + ex.Message;
                }

                return _mainBases[entry.Key] = (template, error);
            }
        }

        /// <summary>The templates of every tier but this one: what an upgrade to this tier takes out of the save (the pieces they place that this one does not).</summary>
        private IReadOnlyList<MainBaseTemplate> OtherTiers(string? tier)
        {
            var key = (MainBaseTiers.FirstOrDefault(t => t.Key == tier) ?? MainBaseTiers.First(t => t.Key == DefaultMainBaseTier)).Key;
            return MainBaseTiers.Where(t => t.Key != key).Select(t => MainBase(t.Key).Template).Where(t => t is not null).Select(t => t!).ToList();
        }

        /// <summary>Every tier's objects together: the area a removal clears, so it takes any tier's base away, not only the one picked.</summary>
        public (MainBaseTemplate? Template, string? Error) MainBaseFootprint()
        {
            var all = MainBaseTiers.Select(t => MainBase(t.Key)).ToList();
            var first = all.FirstOrDefault(a => a.Template is not null).Template;
            if (first is null)
                return (null, all.Select(a => a.Error).FirstOrDefault(e => e is not null) ?? "No Main Base template.");

            return (new MainBaseTemplate { Name = "all tiers", Beacon = first.Beacon, Objects = all.Where(a => a.Template is not null).SelectMany(a => a.Template!.Objects).ToList() }, null);
        }

        /// <summary>Where a Main Base can be built from in this save: the beacons named Base, and outdoor lamps on a foundation.</summary>
        public Task<(IReadOnlyList<BuildBeacon> Anchors, string? Error)> ScanAnchorsAsync(string savePath) => Task.Run(() =>
        {
            var text = ReadText(savePath, out var error);
            return text is null
                ? ((IReadOnlyList<BuildBeacon>)Array.Empty<BuildBeacon>(), error)
                : (BaseBuildingEngine.FindMainBaseAnchors(text), (string?)null);
        });

        /// <summary>The Main Base as this beacon would get it (read-only; null when the save or the template cannot be read).</summary>
        public Task<MainBasePlan?> PlanMainBaseAsync(string savePath, long beaconId, string? tier = null) => Task.Run(() =>
        {
            var (template, _) = MainBase(tier);
            var text = ReadText(savePath, out _);
            return template is null || text is null ? (MainBasePlan?)null : BaseBuildingEngine.PlanMainBase(text, beaconId, template, IsBuilding, OtherTiers(tier));
        });

        /// <summary>
        /// Builds the Main Base into the save: the plan is worked out again from the file as it is right now, then written with a backup
        /// (see <see cref="SaveResupplyService.EditAsync{T}"/>). The game must be at its main menu.
        /// </summary>
        public Task<SaveEdit<MainBaseOutcome>> BuildMainBaseAsync(string savePath, long beaconId, bool prefill, string? tier = null) =>
            _saves.EditAsync<MainBaseOutcome>(savePath, text =>
            {
                var (template, error) = MainBase(tier);
                if (template is null)
                    return ((string?)null, new MainBaseOutcome(null, 0, 0, 0, new[] { error ?? "No template." }), (string?)("Nothing was built. " + error));

                var plan = BaseBuildingEngine.PlanMainBase(text, beaconId, template, IsBuilding, OtherTiers(tier));
                var outcome = BaseBuildingEngine.ApplyMainBase(text, plan, prefill);
                return outcome.Failed ? ((string?)null, outcome, (string?)("Nothing was built. " + string.Join(" ", outcome.Problems))) : (outcome.NewText, outcome, (string?)null);
            }, "building the main base");

        /// <summary>What removing the Main Base around this anchor would take (read-only; null when the save or the template cannot be read).</summary>
        public Task<MainBaseRemovalPlan?> PlanRemoveMainBaseAsync(string savePath, long anchorId) => Task.Run(() =>
        {
            var (template, _) = MainBaseFootprint();
            var text = ReadText(savePath, out _);
            return template is null || text is null ? (MainBaseRemovalPlan?)null : BaseBuildingEngine.PlanRemoveMainBase(text, anchorId, template);
        });

        /// <summary>
        /// Removes the Main Base around this anchor from the save: the plan is worked out again from the file as it is right now, then written with a backup
        /// (see <see cref="SaveResupplyService.EditAsync{T}"/>). The game must be at its main menu.
        /// </summary>
        public Task<SaveEdit<MainBaseRemovalOutcome>> RemoveMainBaseAsync(string savePath, long anchorId) =>
            _saves.EditAsync<MainBaseRemovalOutcome>(savePath, text =>
            {
                var (template, error) = MainBaseFootprint();
                if (template is null)
                {
                    var none = new MainBaseRemovalPlan(null, 0, 0, 0, 0, 0, 0, Array.Empty<RemovedObject>(), 0, 0, Array.Empty<string>(), Array.Empty<long>(), Array.Empty<long>(), new[] { error ?? "No template." });
                    return ((string?)null, new MainBaseRemovalOutcome(null, none, none.Problems), (string?)("Nothing was removed. " + error));
                }

                var outcome = BaseBuildingEngine.RemoveMainBase(text, BaseBuildingEngine.PlanRemoveMainBase(text, anchorId, template));
                return outcome.Failed ? ((string?)null, outcome, (string?)("Nothing was removed. " + string.Join(" ", outcome.Problems))) : (outcome.NewText, outcome, (string?)null);
            }, "removing the main base");

        private BuildPlan MakePlan(string text, long beaconId, BuildTemplate template, BuildRecipe recipe) =>
            recipe.Warehouse ? BaseBuildingEngine.PlanWarehouse(text, beaconId, template, WarehouseRows(), IsBuilding, WarehouseDepth)
            : recipe.Bundles is not null ? BaseBuildingEngine.Plan(text, beaconId, template, recipe.Bundles, IsBuilding)
            : BaseBuildingEngine.Plan(text, beaconId, template, recipe.Items, IsBuilding);

        /// <summary>
        /// Builds the row into the save: the plan is worked out again from the file as it is right now, then written with a backup
        /// (see <see cref="SaveResupplyService.EditAsync{T}"/>). The game must be at its main menu.
        /// </summary>
        public Task<SaveEdit<BuildOutcome>> BuildAsync(string savePath, long beaconId, BuildTemplate template, BuildRecipe recipe, bool setDemand, bool fill) =>
            _saves.EditAsync<BuildOutcome>(savePath, text =>
            {
                var plan = MakePlan(text, beaconId, template, recipe);
                var outcome = BaseBuildingEngine.Apply(text, plan, setDemand, fill);
                return outcome.Failed ? ((string?)null, outcome, (string?)("Nothing was built. " + string.Join(" ", outcome.Problems))) : (outcome.NewText, outcome, (string?)null);
            }, "building the row");
    }
}
