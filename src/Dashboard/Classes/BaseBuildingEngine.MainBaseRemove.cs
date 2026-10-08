using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>One thing the removal would take out of the world: its kind and where it stands (for the preview).</summary>
    public sealed record RemovedObject(string G, double X, double Y, double Z);

    /// <param name="Box">The area cleared: north (x) from/to, east (z) from/to, and the heights from/to, as saved.</param>
    /// <param name="Kept">What is deliberately left standing in the area (the anchor, its foundation, the escape pod).</param>
    public sealed record MainBaseRemovalPlan(
        BuildBeacon? Anchor, double MinX, double MaxX, double MinZ, double MaxZ, double MinY, double MaxY,
        IReadOnlyList<RemovedObject> Objects, int Inventories, int Items, IReadOnlyList<string> Kept,
        IReadOnlyList<long> ObjectIds, IReadOnlyList<long> InventoryIds, IReadOnlyList<string> Problems)
    {
        public bool Any => Objects.Count > 0;
        public bool Ok => Anchor is not null && Problems.Count == 0;
    }

    public sealed record MainBaseRemovalOutcome(string? NewText, MainBaseRemovalPlan Plan, IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>
    /// "Remove main base": takes a Main Base out of a save again. The area is the base's footprint as the template would place it from this anchor (turned the same way),
    /// a little wider than its outermost foundations, from just under the lowest piece to <see cref="ClearHeight"/> above the anchor's foundation. Everything the save
    /// has standing in that area on the anchor's planet goes, whatever it is (the owner's choice: a base cleared out of a save is cleared out), with every inventory
    /// those objects own and every item in them. What stays is the anchor (beacon or lamp), the foundation under it and the escape pod. Written through
    /// <see cref="RemoveByIds"/>, which proves the records left are exactly the original ones minus these.
    /// </summary>
    public static partial class BaseBuildingEngine
    {
        /// <summary>How far above the anchor's foundation the cleared area reaches: ten pod heights.</summary>
        public const double ClearHeight = 60;

        private const double ClearMargin = 3.5;
        private const double ClearBelow = 3;

        public static MainBaseRemovalPlan PlanRemoveMainBase(string text, long anchorId, MainBaseTemplate template)
        {
            MainBaseRemovalPlan Problem(string message) =>
                new(null, 0, 0, 0, 0, 0, 0, Array.Empty<RemovedObject>(), 0, 0, Array.Empty<string>(), Array.Empty<long>(), Array.Empty<long>(), new[] { message });

            var world = Read(text);
            var anchor = MainBaseAnchors(world).FirstOrDefault(b => b.Id == anchorId);
            if (anchor is null)
                return Problem("That beacon or lamp is not in the save.");

            if (anchor.FoundationId is null)
                return Problem("It is not standing on a foundation.");

            var (_, cs, sn) = TurnFor(template, anchor);

            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue, minDy = double.MaxValue;
            foreach (var t in template.Objects)
            {
                var x = anchor.FoundationX + t.Dx * cs + t.Dz * sn;
                var z = anchor.FoundationZ - t.Dx * sn + t.Dz * cs;
                (minX, maxX) = (Math.Min(minX, x), Math.Max(maxX, x));
                (minZ, maxZ) = (Math.Min(minZ, z), Math.Max(maxZ, z));
                minDy = Math.Min(minDy, t.Dy);
            }

            minX -= ClearMargin; maxX += ClearMargin; minZ -= ClearMargin; maxZ += ClearMargin;
            double minY = anchor.FoundationY + minDy - ClearBelow, maxY = anchor.FoundationY + ClearHeight;

            // What every record owns: an object's inventories (liId, siIds) and an inventory's items (woIds), which may own inventories of their own.
            var owns = new Dictionary<long, List<long>>();
            var holds = new Dictionary<long, List<long>>();
            var objectRecords = new HashSet<long>();
            foreach (Match match in RecordPattern.Matches(text))
            {
                try
                {
                    using var doc = JsonDocument.Parse(match.Value);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
                        continue;

                    List<long> Ids(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String
                        ? (e.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => long.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToList()
                        : new List<long>();

                    if (root.TryGetProperty("woIds", out _))
                    {
                        holds[id] = Ids("woIds");
                    }
                    else
                    {
                        objectRecords.Add(id);
                        var inventories = new List<long>();
                        if (root.TryGetProperty("liId", out var li) && li.TryGetInt64(out var liId))
                            inventories.Add(liId);
                        inventories.AddRange(Ids("siIds"));
                        owns[id] = inventories;
                    }
                }
                catch (JsonException)
                {
                    // Not a record we can read; leave it be.
                }
            }

            var kept = new List<string>();
            var doomed = new List<Obj>();
            foreach (var o in world.Objects)
            {
                if (o.X < minX || o.X > maxX || o.Z < minZ || o.Z > maxZ || o.Y < minY || o.Y > maxY)
                    continue;

                if (anchor.PlanetHash != 0 && o.Planet != anchor.PlanetHash)
                    continue;

                if (o.Id == anchor.Id || o.Id == anchor.FoundationId)
                {
                    kept.Add(o.Id == anchor.Id ? o.GId : "the foundation under it");
                    continue;
                }

                if (o.GId == "EscapePod")
                {
                    kept.Add(o.GId);
                    continue;
                }

                doomed.Add(o);
            }

            var objectIds = new HashSet<long>(doomed.Select(o => o.Id));
            var inventoryIds = new HashSet<long>();
            var itemCount = 0;
            var queue = new Queue<long>(doomed.Select(o => o.Id));
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!owns.TryGetValue(id, out var inventories))
                    continue;

                foreach (var inventory in inventories)
                {
                    if (!holds.TryGetValue(inventory, out var itemIds) || !inventoryIds.Add(inventory))
                        continue;

                    foreach (var item in itemIds.Where(i => objectRecords.Contains(i) && objectIds.Add(i)))
                    {
                        itemCount++;
                        queue.Enqueue(item);
                    }
                }
            }

            var shown = doomed.Select(o => new RemovedObject(o.GId, o.X, o.Y, o.Z)).ToList();
            return new MainBaseRemovalPlan(anchor, minX, maxX, minZ, maxZ, minY, maxY, shown, inventoryIds.Count, itemCount,
                kept.Distinct().ToList(), objectIds.ToList(), inventoryIds.ToList(), Array.Empty<string>());
        }

        /// <summary>
        /// Takes the planned objects, their inventories and the items in them out of the text, and checks the result before anyone may write it: the records that remain
        /// are exactly the original ones minus these (see <see cref="RemoveByIds"/>), and the anchor and the foundation under it are still there untouched.
        /// </summary>
        public static MainBaseRemovalOutcome RemoveMainBase(string text, MainBaseRemovalPlan plan)
        {
            if (!plan.Ok)
                return new MainBaseRemovalOutcome(null, plan, plan.Problems.Count > 0 ? plan.Problems : new[] { "There is no beacon or lamp to clear around." });

            if (!plan.Any)
                return new MainBaseRemovalOutcome(null, plan, new[] { "There is nothing standing in the base's area to remove." });

            var (newText, problems) = RemoveByIds(text, plan.ObjectIds, plan.InventoryIds);
            if (newText is null)
                return new MainBaseRemovalOutcome(null, plan, problems);

            var anchorRecords = RecordPattern.Matches(text)
                .Where(m => m.Value.StartsWith("{\"id\":" + plan.Anchor!.Id + ",", StringComparison.Ordinal) || m.Value.StartsWith("{\"id\":" + plan.Anchor.FoundationId + ",", StringComparison.Ordinal))
                .Select(m => m.Value).ToList();
            var after = new HashSet<string>(RecordPattern.Matches(newText).Select(m => m.Value), StringComparer.Ordinal);

            if (anchorRecords.Count != 2 || anchorRecords.Any(r => !after.Contains(r)))
                return new MainBaseRemovalOutcome(null, plan, new[] { "The anchor or the foundation under it would not have been left as it was, so nothing was removed." });

            return new MainBaseRemovalOutcome(newText, plan, Array.Empty<string>());
        }
    }
}
