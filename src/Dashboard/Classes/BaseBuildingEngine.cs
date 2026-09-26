using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// A beacon the owner placed on a foundation, named for what a row should hold ("Fish"). It points the way the row grows.
    /// <paramref name="DirX"/> and <paramref name="DirZ"/> are that direction snapped to the foundation grid (one of them is 0).
    /// </summary>
    public sealed record BuildBeacon(
        long Id, string Text, double X, double Y, double Z, double YawDegrees, int DirX, int DirZ,
        long? FoundationId, double FoundationX, double FoundationY, double FoundationZ);

    /// <summary>One chest of a template: where it stands on its platform (from the platform's centre, in world axes as captured) and how it is turned.</summary>
    public sealed record TemplateChest(double Dx, double Dz, double Dy, string Rot);

    /// <summary>
    /// What one platform carries, captured from a sample the owner built: the chest type and where each chest stands. The offsets are
    /// in the frame of the direction the sample's row ran (DirX, DirZ); applying the template to a row that runs another way turns them.
    /// </summary>
    public sealed record BuildTemplate(string Name, string ChestGId, int Slots, double Spacing, int DirX, int DirZ, List<TemplateChest> Chests);

    /// <summary>
    /// One stocked chest of a "one of each" group (spacesuits, blueprints, ...): a title for its label, the items it holds once each, and an
    /// item to fill the rest of it with (the terra token box). Unlike a labelled chest it has no item filter and is not set to demand anything.
    /// </summary>
    public sealed record BuildBundle(string Title, IReadOnlyList<string> Items, string? FillWith = null);

    /// <param name="Label">The item a labelled chest is for (its gId), or null for a spare or a stocked chest.</param>
    /// <param name="Title">The label of a stocked chest ("Spacesuits"), with <paramref name="Stock"/> and <paramref name="FillWith"/>.</param>
    public sealed record PlannedChest(double X, double Y, double Z, string Rot, string? Label, string? Title = null, IReadOnlyList<string>? Stock = null, string? FillWith = null);

    /// <param name="FoundationExists">A foundation already stands there, so only the chests would be added.</param>
    /// <param name="Conflicts">Things already standing on the platform's square that a build would collide with.</param>
    public sealed record PlannedPlatform(int Index, double X, double Y, double Z, bool FoundationExists, IReadOnlyList<PlannedChest> Chests, IReadOnlyList<string> Conflicts);

    public sealed record BuildPlan(
        BuildBeacon Beacon, BuildTemplate Template, IReadOnlyList<string> Items,
        IReadOnlyList<PlannedPlatform> Platforms, IReadOnlyList<string> Problems)
    {
        public bool Ok => Problems.Count == 0 && Platforms.All(p => p.Conflicts.Count == 0);
        public int ChestCount => Platforms.Sum(p => p.Chests.Count);
        public int Labelled => Platforms.Sum(p => p.Chests.Count(c => c.Label != null || c.Title != null));
    }

    public sealed record CaptureResult(BuildTemplate? Template, IReadOnlyList<string> Problems);

    /// <summary>What Build would write (or did): the new text, and how many of each thing it added. Nothing is returned when there is a problem.</summary>
    public sealed record BuildOutcome(string? NewText, int Foundations, int Chests, int Labelled, int Items, IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>
    /// The Cheats page's Base Building: reads a save for the beacons the owner placed, captures a platform of chests as a template,
    /// and plans a row built from a beacon out in the direction it points, one platform per few chests, each chest labelled with
    /// the next item of a recipe (Fish: Fish1Eggs, Fish2Eggs, ...). This step only reads and plans; nothing is written.
    /// Pure text-in, plan-out, so it is testable without a game or a file.
    ///
    /// What the owner's Custom-1 shows (2026-09-26): a beacon (gId <c>Beacon</c>, text "Fish", rotation 180 degrees about Y) stands 2.519 m
    /// above a foundation, and the platform 6 m along +z carries four Container1 the same height above it. So a beacon rotated 180
    /// points along +z: its direction is the opposite of Unity's forward, (-sin yaw, -cos yaw). One sample, so a plan is refused when
    /// a platform stands where the beacon points and the two disagree.
    /// </summary>
    public static class BaseBuildingEngine
    {
        /// <summary>How far above a foundation's position things placed on it stand (chests and the beacon, in the save).</summary>
        public const double OnFoundation = 2.519;

        private static readonly Regex RecordPattern = new(@"\{(?:[^{}""]|""(?:[^""\\]|\\.)*"")*\}", RegexOptions.Compiled);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed record Obj(long Id, string GId, double X, double Y, double Z, string Rot, string? Text, int? LiId);

        private sealed class World
        {
            public List<Obj> Objects { get; } = new();
            public Dictionary<long, int> InventorySizes { get; } = new();
        }

        private static World Read(string text)
        {
            var world = new World();

            foreach (Match match in RecordPattern.Matches(text))
            {
                try
                {
                    using var doc = JsonDocument.Parse(match.Value);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
                        continue;

                    string? Str(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

                    if (root.TryGetProperty("woIds", out _))
                    {
                        if (root.TryGetProperty("size", out var size) && size.TryGetInt32(out var n))
                            world.InventorySizes[id] = n;
                        continue;
                    }

                    var gId = Str("gId");
                    var pos = Str("pos")?.Split(',');
                    if (gId is null || pos is null || pos.Length != 3
                        || !double.TryParse(pos[0], NumberStyles.Float, Inv, out var x)
                        || !double.TryParse(pos[1], NumberStyles.Float, Inv, out var y)
                        || !double.TryParse(pos[2], NumberStyles.Float, Inv, out var z))
                        continue;

                    int? liId = root.TryGetProperty("liId", out var li) && li.TryGetInt32(out var l) ? l : null;
                    world.Objects.Add(new Obj(id, gId, x, y, z, Str("rot") ?? "0,0,0,1", Str("text"), liId));
                }
                catch (JsonException)
                {
                    // Not a record we can read; leave it be.
                }
            }

            return world;
        }

        /// <summary>The yaw of a rotation quaternion "x,y,z,w", in degrees (Unity: clockwise from above, 0 along +z).</summary>
        public static double YawOf(string rot)
        {
            var q = rot.Split(',').Select(s => double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : 0).ToArray();
            if (q.Length != 4)
                return 0;

            var (x, y, z, w) = (q[0], q[1], q[2], q[3]);
            return Math.Atan2(2 * (w * y + x * z), 1 - 2 * (y * y + z * z)) * 180 / Math.PI;
        }

        /// <summary>Turns a rotation by <paramref name="degrees"/> about the vertical axis (world space), written the way the save writes them.</summary>
        public static string TurnRot(string rot, int degrees)
        {
            degrees = ((degrees % 360) + 360) % 360;
            if (degrees == 0)
                return rot;

            var q = rot.Split(',').Select(s => double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : 0).ToArray();
            if (q.Length != 4)
                return rot;

            var half = degrees * Math.PI / 360;
            double ay = Math.Sin(half), aw = Math.Cos(half);
            var (bx, by, bz, bw) = (q[0], q[1], q[2], q[3]);

            // a = (0, ay, 0, aw) times b
            var result = new[]
            {
                aw * bx + ay * bz,
                aw * by + ay * bw,
                aw * bz - ay * bx,
                aw * bw - ay * by
            };

            return string.Join(",", result.Select(v => Math.Abs(v) < 1e-7 ? "0" : Math.Round(v, 7).ToString("R", Inv)));
        }

        /// <summary>The direction a beacon points, snapped to the grid: the opposite of its forward, (-sin yaw, -cos yaw).</summary>
        public static (int DirX, int DirZ) Snap(double yawDegrees)
        {
            var r = yawDegrees * Math.PI / 180;
            double dx = -Math.Sin(r), dz = -Math.Cos(r);
            return Math.Abs(dx) > Math.Abs(dz) ? (Math.Sign(dx), 0) : (0, Math.Sign(dz));
        }

        public static IReadOnlyList<BuildBeacon> FindBeacons(string text)
        {
            var world = Read(text);
            return Beacons(world);
        }

        private static List<BuildBeacon> Beacons(World world)
        {
            var foundations = world.Objects.Where(o => o.GId == "Foundation").ToList();
            var result = new List<BuildBeacon>();

            foreach (var b in world.Objects.Where(o => o.GId == "Beacon" && !string.IsNullOrWhiteSpace(o.Text)))
            {
                var under = foundations
                    .Where(f => Math.Abs(f.X - b.X) < 1 && Math.Abs(f.Z - b.Z) < 1 && b.Y - f.Y is > 1 and < 4)
                    .OrderBy(f => Math.Abs(f.X - b.X) + Math.Abs(f.Z - b.Z))
                    .FirstOrDefault();

                var yaw = YawOf(b.Rot);
                var (dx, dz) = Snap(yaw);
                result.Add(new BuildBeacon(b.Id, b.Text!.Trim(), b.X, b.Y, b.Z, yaw, dx, dz,
                    under?.Id, under?.X ?? b.X, under?.Y ?? b.Y - OnFoundation, under?.Z ?? b.Z));
            }

            return result.OrderBy(b => b.Text, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Reads a template from a sample: the platform the beacon points at, and the chests standing on it. Refuses when there is
        /// no platform there, no chests on it, or the chests are of more than one type.
        /// </summary>
        public static CaptureResult Capture(string text, long beaconId, string name)
        {
            var world = Read(text);
            var beacon = Beacons(world).FirstOrDefault(b => b.Id == beaconId);
            if (beacon is null)
                return new CaptureResult(null, new[] { "That beacon is not in the save." });

            if (beacon.FoundationId is null)
                return new CaptureResult(null, new[] { "The beacon is not standing on a foundation." });

            var (dx, dz) = (beacon.DirX, beacon.DirZ);
            var next = world.Objects
                .Where(o => o.GId == "Foundation")
                .Select(f => (F: f, Along: (f.X - beacon.FoundationX) * dx + (f.Z - beacon.FoundationZ) * dz,
                                    Across: Math.Abs((f.X - beacon.FoundationX) * -dz + (f.Z - beacon.FoundationZ) * dx)))
                .Where(t => t.Along is > 2 and < 10 && t.Across < 1 && Math.Abs(t.F.Y - beacon.FoundationY) < 0.6)
                .OrderBy(t => t.Along)
                .FirstOrDefault();

            if (next.F is null)
                return new CaptureResult(null, new[] { "No platform stands where the beacon points, so there is nothing to copy." });

            var spacing = Math.Round(next.Along, 2);
            var half = spacing / 2;
            var chests = world.Objects
                .Where(o => o.GId.StartsWith("Container", StringComparison.Ordinal)
                            && Math.Abs(o.X - next.F.X) <= half && Math.Abs(o.Z - next.F.Z) <= half
                            && Math.Abs(o.Y - next.F.Y - OnFoundation) < 0.8)
                .ToList();

            if (chests.Count == 0)
                return new CaptureResult(null, new[] { "The platform the beacon points at carries no chests." });

            if (chests.Select(c => c.GId).Distinct().Count() > 1)
                return new CaptureResult(null, new[] { "The chests on that platform are of more than one type: " + string.Join(", ", chests.Select(c => c.GId).Distinct()) });

            var slots = chests[0].LiId is { } li && world.InventorySizes.TryGetValue(li, out var size) ? size : 0;

            // Reading order along the row, then across it, so labels come out in a predictable order.
            var ordered = chests
                .Select(c => (C: c, Along: (c.X - next.F.X) * dx + (c.Z - next.F.Z) * dz, Across: (c.X - next.F.X) * -dz + (c.Z - next.F.Z) * dx))
                .OrderBy(t => Math.Round(t.Along, 1)).ThenBy(t => Math.Round(t.Across, 1))
                .Select(t => new TemplateChest(Math.Round(t.C.X - next.F.X, 3), Math.Round(t.C.Z - next.F.Z, 3), Math.Round(t.C.Y - next.F.Y, 3), t.C.Rot))
                .ToList();

            return new CaptureResult(new BuildTemplate(name, chests[0].GId, slots, spacing, dx, dz, ordered), Array.Empty<string>());
        }

        // The save's layout (seen in Custom-1): sections joined by CR @ CR, and inside a section records joined by "|" and a newline.
        // Objects come first, inventories after them. New objects go in front of the beacon's own record, new inventories in front of
        // the last inventory's, so each lands inside its own section at a record boundary.
        private static readonly Regex Planet = new(@"""planet"":(-?\d+)", RegexOptions.Compiled);
        private static readonly Random Rng = new();
        private const long ObjectIdMin = 200_000_000, ObjectIdMax = 210_000_000;

        /// <summary>
        /// Writes a plan into the text of a save: a foundation for each new platform, and on every platform the template's chests,
        /// each with its own empty inventory, labelled (text) and filtered (liGrps) with its item. With <paramref name="setDemand"/>
        /// every labelled chest also demands that item; with <paramref name="fill"/> every labelled chest is filled to its slot count with
        /// that item (new item records, like Resupply's, in front of the beacon's own record). Nothing else in the text changes: the proof is that taking out what was added
        /// gives back the original exactly. Any problem means no new text at all.
        /// </summary>
        public static BuildOutcome Apply(string text, BuildPlan plan, bool setDemand, bool fill = false, Random? random = null)
        {
            random ??= Rng;

            if (plan.Problems.Count > 0)
                return Failed(plan.Problems.ToList());

            var conflict = plan.Platforms.FirstOrDefault(p => p.Conflicts.Count > 0);
            if (conflict is not null)
                return Failed($"Platform {conflict.Index} is not clear ({string.Join(", ", conflict.Conflicts)}), so nothing was built.");

            // Every record with its place in the text.
            var records = new List<(int Start, string Raw, long Id, bool IsInventory)>();
            foreach (Match match in RecordPattern.Matches(text))
            {
                try
                {
                    using var doc = JsonDocument.Parse(match.Value);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                        records.Add((match.Index, match.Value, id, doc.RootElement.TryGetProperty("woIds", out _)));
                }
                catch (JsonException ex)
                {
                    return Failed($"A record at position {match.Index} is not valid JSON: {ex.Message}");
                }
            }

            var beacon = records.FirstOrDefault(r => !r.IsInventory && r.Id == plan.Beacon.Id && r.Raw.Contains("\"gId\":\"Beacon\"", StringComparison.Ordinal));
            var inventories = records.Where(r => r.IsInventory).ToList();
            if (beacon.Raw is null || inventories.Count == 0)
                return Failed("The beacon (or the save's inventories) could not be found in the text, so nothing was built.");

            var planet = Planet.Match(beacon.Raw).Groups[1].Value;
            if (planet.Length == 0)
                return Failed("The beacon has no planet, so nothing was built.");

            var used = new HashSet<long>(records.Select(r => r.Id));
            var nextInventory = inventories.Max(r => r.Id);
            var eol = text.Contains("|\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var slots = plan.Template.Slots > 0 ? plan.Template.Slots : 15;

            long NewObjectId()
            {
                while (true)
                {
                    var id = random.NextInt64(ObjectIdMin, ObjectIdMax);
                    if (used.Add(id))
                        return id;
                }
            }

            string Pos(double x, double y, double z) => string.Join(",", new[] { x, y, z }.Select(v => Math.Round(v, 4).ToString("0.####", Inv)));

            var newObjects = new List<string>();
            var newInventories = new List<string>();
            var objectIds = new List<long>();
            var inventoryIds = new List<long>();
            var chestLinks = new List<long>();
            var foundations = 0;
            var labelled = 0;
            var items = 0;

            foreach (var platform in plan.Platforms)
            {
                if (!platform.FoundationExists)
                {
                    var foundationId = NewObjectId();
                    objectIds.Add(foundationId);
                    newObjects.Add($"{{\"id\":{foundationId},\"gId\":\"Foundation\",\"pos\":\"{Pos(platform.X, platform.Y, platform.Z)}\",\"rot\":\"0,0,0,1\",\"planet\":{planet}}}");
                    foundations++;
                }

                foreach (var chest in platform.Chests)
                {
                    do { nextInventory++; } while (!used.Add(nextInventory));

                    var demand = setDemand && chest.Label is not null ? $",\"demandGrps\":\"{chest.Label}\",\"supplyGrps\":\"\",\"priority\":0" : "";

                    // A stocked chest holds one of each item of its bundle, and its fill item to capacity; a filled labelled chest holds one item record
                    // per slot. Each is a new object of its own.
                    var held = new List<long>();
                    if (chest.Title is not null)
                    {
                        var stock = (chest.Stock ?? Array.Empty<string>()).Concat(chest.FillWith is null ? Array.Empty<string>() : Enumerable.Repeat(chest.FillWith, Math.Max(0, slots - (chest.Stock?.Count ?? 0)))).ToList();
                        if (stock.Count > slots)
                            return Failed($"\"{chest.Title}\" has {stock.Count} items but its chest holds {slots}, so nothing was built.");

                        foreach (var gId in stock)
                        {
                            var itemId = NewObjectId();
                            objectIds.Add(itemId);
                            held.Add(itemId);
                            newObjects.Add($"{{\"id\":{itemId},\"gId\":\"{gId}\"}}");
                        }

                        items += held.Count;
                    }
                    else if (fill && chest.Label is not null)
                    {
                        for (var n = 0; n < slots; n++)
                        {
                            var itemId = NewObjectId();
                            objectIds.Add(itemId);
                            held.Add(itemId);
                            newObjects.Add($"{{\"id\":{itemId},\"gId\":\"{chest.Label}\"}}");
                        }

                        items += held.Count;
                    }

                    newInventories.Add($"{{\"id\":{nextInventory},\"woIds\":\"{string.Join(",", held)}\",\"size\":{slots}{demand}}}");
                    inventoryIds.Add(nextInventory);
                    chestLinks.Add(nextInventory);

                    var liGrps = chest.Label is not null ? $",\"liGrps\":\"{chest.Label}\"" : "";
                    var label = chest.Label is not null ? $",\"text\":\"{chest.Label}\"" : chest.Title is not null ? $",\"text\":\"{chest.Title}\"" : "";
                    var chestId = NewObjectId();
                    objectIds.Add(chestId);
                    newObjects.Add($"{{\"id\":{chestId},\"gId\":\"{plan.Template.ChestGId}\",\"liId\":{nextInventory}{liGrps},\"pos\":\"{Pos(chest.X, chest.Y, chest.Z)}\",\"rot\":\"{chest.Rot}\",\"planet\":{planet}{label}}}");

                    if (chest.Label is not null || chest.Title is not null)
                        labelled++;
                }
            }

            var objectText = string.Concat(newObjects.Select(r => r + "|" + eol));
            var inventoryText = string.Concat(newInventories.Select(r => r + "|" + eol));
            var lastInventory = inventories.OrderBy(r => r.Start).Last();

            // The inventory insertion is later in the text, so it goes in first and the earlier one keeps its place.
            var newText = text.Insert(lastInventory.Start, inventoryText).Insert(beacon.Start, objectText);

            var problems = VerifyBuild(text, newText, objectText, inventoryText, objectIds, inventoryIds, chestLinks);
            return problems.Count > 0 ? Failed(problems) : new BuildOutcome(newText, foundations, plan.ChestCount, labelled, items, Array.Empty<string>());
        }

        private static BuildOutcome Failed(string problem) => Failed(new List<string> { problem });

        private static BuildOutcome Failed(List<string> problems) => new(null, 0, 0, 0, 0, problems);

        /// <summary>
        /// Checks what was written before anyone is allowed to write it. Only the new records are judged: real saves already hold
        /// ids that appear twice (an object and an inventory can share a number by coincidence) and containers that point at
        /// inventories kept elsewhere, and none of that is ours to flag.
        /// </summary>
        private static List<string> VerifyBuild(string before, string after, string objectText, string inventoryText,
            List<long> objectIds, List<long> inventoryIds, List<long> chestLinks)
        {
            var problems = new List<string>();

            // The strong check: take out exactly what was added and the original must come back byte for byte.
            var undone = after;
            var o = undone.IndexOf(objectText, StringComparison.Ordinal);
            if (o < 0) problems.Add("The added objects were not found in the result.");
            else undone = undone.Remove(o, objectText.Length);

            var i = undone.IndexOf(inventoryText, StringComparison.Ordinal);
            if (i < 0) problems.Add("The added inventories were not found in the result.");
            else undone = undone.Remove(i, inventoryText.Length);

            if (problems.Count == 0 && !string.Equals(undone, before, StringComparison.Ordinal))
                problems.Add("Taking out what was added does not give back the original save, so something else changed. Nothing was written.");

            // What the game will read: every record still valid JSON, and each new id present exactly once.
            var seen = new Dictionary<long, int>();
            var wanted = new HashSet<long>(objectIds.Concat(inventoryIds));

            foreach (Match match in RecordPattern.Matches(after))
            {
                try
                {
                    using var doc = JsonDocument.Parse(match.Value);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("id", out var idElement)
                        && idElement.TryGetInt64(out var id) && wanted.Contains(id))
                        seen[id] = seen.GetValueOrDefault(id) + 1;
                }
                catch (JsonException ex)
                {
                    problems.Add($"A record is not valid JSON after the edit: {ex.Message}");
                    break;
                }
            }

            foreach (var id in wanted)
            {
                var n = seen.GetValueOrDefault(id);
                if (n != 1)
                    problems.Add($"The new id {id} appears {n} times after the edit.");
            }

            // Every new chest points at one of the new inventories, and each of those is used once.
            var links = new HashSet<long>(inventoryIds);
            foreach (var link in chestLinks.Where(l => !links.Contains(l)))
                problems.Add($"A new container points at inventory {link}, which was not added.");

            if (chestLinks.Count != chestLinks.Distinct().Count())
                problems.Add("Two new containers share an inventory.");

            return problems.Take(5).ToList();
        }
        /// <param name="items">The gIds to label the chests with, in order.</param>
        /// <param name="isBuilding">Whether an object of this gId is something a new platform must not be built on top of.</param>
        public static BuildPlan Plan(string text, long beaconId, BuildTemplate template, IReadOnlyList<string> items, Func<string, bool> isBuilding) =>
            PlanCore(text, beaconId, template, items, null, isBuilding);

        /// <summary>A plan for stocked chests: one chest per bundle, each holding one of each of its items. The template's chests must be big enough for the largest bundle.</summary>
        public static BuildPlan Plan(string text, long beaconId, BuildTemplate template, IReadOnlyList<BuildBundle> bundles, Func<string, bool> isBuilding) =>
            PlanCore(text, beaconId, template, bundles.Select(b => b.Title).ToList(), bundles, isBuilding);

        private static BuildPlan PlanCore(string text, long beaconId, BuildTemplate template, IReadOnlyList<string> items, IReadOnlyList<BuildBundle>? bundles, Func<string, bool> isBuilding)
        {
            var world = Read(text);
            var beacon = Beacons(world).FirstOrDefault(b => b.Id == beaconId);
            var problems = new List<string>();

            if (beacon is null)
                return new BuildPlan(new BuildBeacon(beaconId, "", 0, 0, 0, 0, 0, 0, null, 0, 0, 0), template, items, Array.Empty<PlannedPlatform>(), new[] { "That beacon is not in the save." });

            if (beacon.FoundationId is null)
                problems.Add("The beacon is not standing on a foundation.");

            // Anything behind the beacon (the platforms it stands among) does not matter. What matters is ahead of it, in the direction it
            // points, which is checked platform by platform below.

            var perPlatform = template.Chests.Count;
            if (perPlatform == 0)
                problems.Add("The template has no chests.");

            if (items.Count == 0)
                problems.Add("There is nothing to build for.");

            // A stocked chest has to hold everything in its bundle.
            if (bundles is not null && bundles.Count > 0)
            {
                var biggest = bundles.OrderByDescending(b => b.Items.Count).First();
                if (biggest.Items.Count > template.Slots)
                    problems.Add($"\"{biggest.Title}\" has {biggest.Items.Count} items but the {template.Name} template's chests hold {template.Slots}. Use a template with more slots (Container2 or Container3).");
            }

            if (problems.Count > 0)
                return new BuildPlan(beacon, template, items, Array.Empty<PlannedPlatform>(), problems);

            // Turn the template from the direction it was captured in to the one this beacon points.
            int c0 = template.DirX * beacon.DirX + template.DirZ * beacon.DirZ;
            int s0 = template.DirX * beacon.DirZ - template.DirZ * beacon.DirX;
            var turn = (int)Math.Round(-Math.Atan2(s0, c0) * 180 / Math.PI);

            var count = (int)Math.Ceiling(items.Count / (double)perPlatform);
            var half = template.Spacing / 2;
            var platforms = new List<PlannedPlatform>();
            var next = 0;

            for (var k = 1; k <= count; k++)
            {
                var (fx, fy, fz) = (beacon.FoundationX + k * template.Spacing * beacon.DirX, beacon.FoundationY, beacon.FoundationZ + k * template.Spacing * beacon.DirZ);

                var exists = world.Objects.Any(o => o.GId == "Foundation" && Math.Abs(o.X - fx) < 0.6 && Math.Abs(o.Z - fz) < 0.6 && Math.Abs(o.Y - fy) < 0.6);

                var conflicts = world.Objects
                    .Where(o => o.Id != beacon.Id && isBuilding(o.GId)
                                && Math.Abs(o.X - fx) < half - 0.05 && Math.Abs(o.Z - fz) < half - 0.05
                                && o.Y > fy - 1 && o.Y < fy + 8
                                && !(o.GId == "Foundation" && Math.Abs(o.X - fx) < 0.6 && Math.Abs(o.Z - fz) < 0.6 && Math.Abs(o.Y - fy) < 0.6))
                    .Take(4)
                    .Select(o => $"{o.GId} at ({o.X:0.#}, {o.Z:0.#})")
                    .ToList();

                // Where each chest ends up: the template's offset turned to this row's direction, and the chest turned with it.
                var (cs, sn) = (Math.Round(Math.Cos(turn * Math.PI / 180)), Math.Round(Math.Sin(turn * Math.PI / 180)));
                var placed = template.Chests.Select(c =>
                {
                    // Positive turns carry +z toward +x: (x, z) -> (x cos + z sin, -x sin + z cos).
                    var ox = c.Dx * cs + c.Dz * sn;
                    var oz = -c.Dx * sn + c.Dz * cs;
                    return (Ox: ox, Oz: oz, c.Dy, Rot: TurnRot(c.Rot, turn));
                }).ToList();

                // Labels go in the order you would read the platform standing at the beacon, looking down the row: nearest first, and
                // across each pair from the LEFT chest to the RIGHT one. So a recipe that lists an item and then its rod puts the
                // item on the left and the rod beside it on the right, and Fish1Eggs is the nearest left chest. Left is (-dirZ, dirX):
                // for a row going north (+x) it is +z, the west side (east is -z).
                var ordered = placed
                    .OrderBy(p => Math.Round(p.Ox * beacon.DirX + p.Oz * beacon.DirZ, 1))
                    .ThenByDescending(p => Math.Round(-p.Ox * beacon.DirZ + p.Oz * beacon.DirX, 1));

                var chests = new List<PlannedChest>();
                foreach (var p in ordered)
                {
                    var (px, py, pz) = (Math.Round(fx + p.Ox, 3), Math.Round(fy + p.Dy, 3), Math.Round(fz + p.Oz, 3));
                    if (bundles is not null)
                        chests.Add(next < bundles.Count
                            ? new PlannedChest(px, py, pz, p.Rot, null, bundles[next].Title, bundles[next].Items, bundles[next].FillWith)
                            : new PlannedChest(px, py, pz, p.Rot, null));
                    else
                        chests.Add(new PlannedChest(px, py, pz, p.Rot, next < items.Count ? items[next] : null));
                    next++;
                }

                platforms.Add(new PlannedPlatform(k, fx, fy, fz, exists, chests, conflicts));
            }

            return new BuildPlan(beacon, template, items, platforms, problems);
        }
    }
}
