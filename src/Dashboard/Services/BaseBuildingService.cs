using System.Text;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>A family of items a row of chests is labelled with, in order: "Fish" is Fish1Eggs, Fish2Eggs, ...</summary>
    public sealed record BuildRecipe(string Key, string Name, IReadOnlyList<string> Items, IReadOnlyList<BuildBundle>? Bundles = null)
    {
        /// <summary>How many chests the group is: one per item, or one per bundle for a "one of each" group.</summary>
        public int ChestCount => Bundles?.Count ?? Items.Count;

        /// <summary>The most a stocked chest must hold, so which templates are big enough (0 for an ordinary group).</summary>
        public int MinSlots => Bundles is { Count: > 0 } ? Bundles.Max(b => b.Items.Count) : 0;
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

        public BaseBuildingService(ItemCatalog catalog, SaveResupplyService saves)
        {
            _catalog = catalog;
            _saves = saves;
        }

        // Something a new platform must not be built on top of: any building piece, machine or container.
        private bool IsBuilding(string gId) => _catalog.CategoryOf(gId) is ItemCategory.Machine or ItemCategory.BasePart or ItemCategory.Container
            or ItemCategory.WorldMarker or ItemCategory.Wreck;

        public IReadOnlyList<BuildRecipe> Recipes()
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
                    .ToList();

                return new BuildRecipe(f.Key, f.Name, items);
            }).Where(r => r.Items.Count > 0).Append(GearRecipe(known)).Where(r => r.ChestCount > 0).ToList();
        }

        private static readonly string[] GearWords = { "gear", "equipment", "suit", "suits", "spacesuit", "spacesuits", "blueprint", "blueprints", "token", "tokens", "loadout" };

        // The "one of each" group: spacesuits, personal equipment, vehicle equipment and blueprints go one of each into their own chest, and the
        // terra token box is filled with 5,000 tokens. Read from the item table by type, so a new suit or upgrade joins by itself.
        private BuildRecipe GearRecipe(HashSet<string> known)
        {
            IReadOnlyList<string> Of(Func<string, bool> pick) => known.Where(pick).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
            int Number(string g) => int.TryParse(Regex.Match(g, @"\d+").Value, out var n) ? n : 0;

            var suits = known.Where(g => _catalog.TypeOf(g) == ItemType.Spacesuit).OrderBy(Number).ToList();
            var personal = Of(g => _catalog.TypeOf(g) == ItemType.Tool).Concat(Of(g => _catalog.TypeOf(g) == ItemType.Gear && !g.StartsWith("BlueprintT", StringComparison.Ordinal))).ToList();
            var vehicle = Of(g => _catalog.TypeOf(g) == ItemType.Part);
            var blueprints = known.Where(g => Regex.IsMatch(g, @"^BlueprintT\d+$")).OrderBy(Number).ToList();

            var bundles = new List<BuildBundle>();
            if (suits.Count > 0) bundles.Add(new BuildBundle("Spacesuits", suits));
            if (personal.Count > 0) bundles.Add(new BuildBundle("Personal equipment", personal));
            if (vehicle.Count > 0) bundles.Add(new BuildBundle("Vehicle equipment", vehicle));
            if (blueprints.Count > 0) bundles.Add(new BuildBundle("Blueprints", blueprints));
            if (known.Contains("TerraTokens5000")) bundles.Add(new BuildBundle("Terra tokens", Array.Empty<string>(), "TerraTokens5000"));

            return new BuildRecipe("gear", "Equipment and tokens (one of each)", bundles.SelectMany(b => b.Items).ToList(), bundles);
        }

        /// <summary>The recipe a beacon's name asks for ("Fish", "fish eggs", "Butterflies"), or null.</summary>
        public BuildRecipe? RecipeFor(string beaconText)
        {
            var word = new string(beaconText.Where(char.IsLetter).ToArray()).ToLowerInvariant();

            if (GearWords.Any(w => word == w || word.StartsWith(w, StringComparison.Ordinal)))
                return Recipes().FirstOrDefault(r => r.Key == "gear");

            foreach (var f in Families)
            {
                if (f.Words.Any(w => word == w || word.StartsWith(w, StringComparison.Ordinal)))
                    return Recipes().FirstOrDefault(r => r.Key == f.Key);
            }

            return null;
        }

        private static string? ReadText(string path, out string? error)
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

            return recipe.Bundles is not null
                ? BaseBuildingEngine.Plan(text, beaconId, template, recipe.Bundles, IsBuilding)
                : BaseBuildingEngine.Plan(text, beaconId, template, recipe.Items, IsBuilding);
        });

        /// <summary>
        /// Builds the row into the save: the plan is worked out again from the file as it is right now, then written with a backup
        /// (see <see cref="SaveResupplyService.EditAsync{T}"/>). The game must be at its main menu.
        /// </summary>
        public Task<SaveEdit<BuildOutcome>> BuildAsync(string savePath, long beaconId, BuildTemplate template, BuildRecipe recipe, bool setDemand, bool fill) =>
            _saves.EditAsync<BuildOutcome>(savePath, text =>
            {
                var plan = recipe.Bundles is not null
                    ? BaseBuildingEngine.Plan(text, beaconId, template, recipe.Bundles, IsBuilding)
                    : BaseBuildingEngine.Plan(text, beaconId, template, recipe.Items, IsBuilding);
                var outcome = BaseBuildingEngine.Apply(text, plan, setDemand, fill);
                return outcome.Failed ? ((string?)null, outcome, (string?)("Nothing was built. " + string.Join(" ", outcome.Problems))) : (outcome.NewText, outcome, (string?)null);
            }, "building the row");
    }
}
