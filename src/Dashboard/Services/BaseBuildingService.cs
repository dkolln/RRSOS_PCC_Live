using System.Text;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>A family of items a row of chests is labelled with, in order: "Fish" is Fish1Eggs, Fish2Eggs, ...</summary>
    public sealed record BuildRecipe(string Key, string Name, IReadOnlyList<string> Items);

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
            ("tree", "Tree seeds", new[] { "tree", "trees" }, new Regex(@"^Tree(\d+)Seed$", RegexOptions.Compiled), Array.Empty<string>())
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
                    .OrderBy(t => int.Parse(t.M.Groups[1].Value))
                    .Select(t => t.G)
                    .Concat(f.Extra.Where(known.Contains))
                    .ToList();

                return new BuildRecipe(f.Key, f.Name, items);
            }).Where(r => r.Items.Count > 0).ToList();
        }

        /// <summary>The recipe a beacon's name asks for ("Fish", "fish eggs", "Butterflies"), or null.</summary>
        public BuildRecipe? RecipeFor(string beaconText)
        {
            var word = new string(beaconText.Where(char.IsLetter).ToArray()).ToLowerInvariant();

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

        public Task<BuildPlan?> PlanAsync(string savePath, long beaconId, BuildTemplate template, IReadOnlyList<string> items) => Task.Run(() =>
        {
            var text = ReadText(savePath, out _);
            if (text is null)
                return (BuildPlan?)null;

            return BaseBuildingEngine.Plan(text, beaconId, template, items, IsBuilding);
        });

        /// <summary>
        /// Builds the row into the save: the plan is worked out again from the file as it is right now, then written with a backup
        /// (see <see cref="SaveResupplyService.EditAsync{T}"/>). The game must be at its main menu.
        /// </summary>
        public Task<SaveEdit<BuildOutcome>> BuildAsync(string savePath, long beaconId, BuildTemplate template, IReadOnlyList<string> items, bool setDemand, bool fill) =>
            _saves.EditAsync<BuildOutcome>(savePath, text =>
            {
                var plan = BaseBuildingEngine.Plan(text, beaconId, template, items, IsBuilding);
                var outcome = BaseBuildingEngine.Apply(text, plan, setDemand, fill);
                return outcome.Failed ? ((string?)null, outcome, (string?)("Nothing was built. " + string.Join(" ", outcome.Problems))) : (outcome.NewText, outcome, (string?)null);
            }, "building the row");
    }
}
