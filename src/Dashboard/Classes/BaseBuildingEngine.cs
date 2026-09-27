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
    /// <param name="SkipUnlocked">
    /// The items are blueprint chips written "chip@building" (a chip linked to the building it unlocks), and any whose building the save has already
    /// unlocked is left out.
    /// </param>
    /// <param name="Split">
    /// When the items do not fit one chest of the template, they are shared over as many chests as needed ("Blueprints (1 of 3)"), each a full one in
    /// order, instead of the plan being refused.
    /// </param>
    public sealed record BuildBundle(string Title, IReadOnlyList<string> Items, string? FillWith = null, bool SkipUnlocked = false, bool Split = false);

    /// <param name="Label">The item a labelled chest is for (its gId), or null for a spare or a stocked chest.</param>
    /// <param name="Title">The label of a stocked chest ("Spacesuits"), with <paramref name="Stock"/> and <paramref name="FillWith"/>.</param>
    public sealed record PlannedChest(double X, double Y, double Z, string Rot, string? Label, string? Title = null, IReadOnlyList<string>? Stock = null, string? FillWith = null);

    /// <summary>A sign put on a chest: where it hangs, how it is turned, and what it says.</summary>
    public sealed record PlannedSign(double X, double Y, double Z, string Rot, string Text);

    /// <param name="FoundationExists">A foundation already stands there, so only the chests would be added.</param>
    /// <param name="Conflicts">Things already standing on the platform's square that a build would collide with.</param>
    /// <param name="Group">The group of a warehouse this platform belongs to, and <c>Row</c> its row (1 for the first); a bare filler foundation is "(filler)", an aisle row "(blank)".</param>
    /// <param name="Signs">Signs to hang on this platform's chests (the index of a warehouse row).</param>
    public sealed record PlannedPlatform(int Index, double X, double Y, double Z, bool FoundationExists, IReadOnlyList<PlannedChest> Chests, IReadOnlyList<string> Conflicts, string? Group = null, int Row = 0, IReadOnlyList<PlannedSign>? Signs = null);

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
    public sealed record BuildOutcome(string? NewText, int Foundations, int Chests, int Labelled, int Items, IReadOnlyList<string> Problems, int Signs = 0, int LocalChests = 0)
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
    public static partial class BaseBuildingEngine
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
            var signs = 0;

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
                            // A blueprint chip is written "chip@building": the item is the chip, linked (liGrps) to the building it unlocks.
                            var at = gId.IndexOf('@');
                            newObjects.Add(at < 0
                                ? $"{{\"id\":{itemId},\"gId\":\"{gId}\"}}"
                                : $"{{\"id\":{itemId},\"gId\":\"{gId[..at]}\",\"liGrps\":\"{gId[(at + 1)..]}\"}}");
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

                // Signs hung on this platform's chests (the index of a warehouse row): each is an object of its own.
                foreach (var sign in platform.Signs ?? Array.Empty<PlannedSign>())
                {
                    var signId = NewObjectId();
                    objectIds.Add(signId);
                    newObjects.Add($"{{\"id\":{signId},\"gId\":\"Sign\",\"pos\":\"{Pos(sign.X, sign.Y, sign.Z)}\",\"rot\":\"{sign.Rot}\",\"planet\":{planet},\"text\":\"{sign.Text}\"}}");
                    signs++;
                }
            }

            var objectText = string.Concat(newObjects.Select(r => r + "|" + eol));
            var inventoryText = string.Concat(newInventories.Select(r => r + "|" + eol));
            var lastInventory = inventories.OrderBy(r => r.Start).Last();

            // The inventory insertion is later in the text, so it goes in first and the earlier one keeps its place.
            var newText = text.Insert(lastInventory.Start, inventoryText).Insert(beacon.Start, objectText);

            var problems = VerifyBuild(text, newText, objectText, inventoryText, objectIds, inventoryIds, chestLinks);
            return problems.Count > 0 ? Failed(problems) : new BuildOutcome(newText, foundations, plan.ChestCount, labelled, items, Array.Empty<string>(), signs);
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

        /// <summary>What one chest of a platform is for: an item to label it with, a stocked bundle, or neither (a spare).</summary>
        private readonly record struct Cell(string? Label, BuildBundle? Bundle);

        /// <summary>The cell for an item of a recipe: a chest labelled and filtered with it, nothing (null), or, for "=Name", a holding tank titled Name (no filter, demand or contents).</summary>
        private static Cell ItemCell(string? item) =>
            item is null ? default
            : item.StartsWith('=') ? new Cell(null, new BuildBundle(item[1..], Array.Empty<string>()))
            : new Cell(item, null);

        /// <summary>
        /// Drops the blueprint chips of buildings the save has already unlocked, and shares a bundle that may be split over as many chests as the template's
        /// slots need ("Blueprints (1 of 2)").
        /// </summary>
        private static List<BuildBundle> PrepareBundles(string text, BuildTemplate template, IReadOnlyList<BuildBundle> bundles)
        {
            var result = bundles.ToList();

            // Blueprint chips for buildings this save has already unlocked are not needed.
            if (result.Any(b => b.SkipUnlocked))
            {
                var unlocked = new HashSet<string>((Regex.Match(text, "\"unlockedGroups\":\"([^\"]*)\"").Groups[1].Value).Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                result = result.Select(b => b.SkipUnlocked ? b with { Items = b.Items.Where(i => !unlocked.Contains(i.Contains('@') ? i[(i.IndexOf('@') + 1)..] : i)).ToList() } : b).ToList();
            }

            // A bundle that may be split is shared over as many chests as the template's slots need.
            if (template.Slots > 0 && result.Any(b => b.Split && b.Items.Count > template.Slots))
            {
                result = result.SelectMany(b =>
                {
                    if (!b.Split || b.Items.Count <= template.Slots)
                        return new[] { b };

                    var parts = (int)Math.Ceiling(b.Items.Count / (double)template.Slots);
                    return Enumerable.Range(0, parts)
                        .Select(i => b with { Title = $"{b.Title} ({i + 1} of {parts})", Items = b.Items.Skip(i * template.Slots).Take(template.Slots).ToList() })
                        .ToArray();
                }).ToList();
            }

            return result;
        }

        /// <summary>The turn (degrees) that carries the template from the direction it was captured in to the one this beacon points.</summary>
        private static int TurnFor(BuildTemplate template, BuildBeacon beacon)
        {
            int c0 = template.DirX * beacon.DirX + template.DirZ * beacon.DirZ;
            int s0 = template.DirX * beacon.DirZ - template.DirZ * beacon.DirX;
            return (int)Math.Round(-Math.Atan2(s0, c0) * 180 / Math.PI);
        }

        /// <summary>
        /// One platform at (fx, fy, fz): whether a foundation already stands there, what would collide with a new one, and the template chests turned to the
        /// beacon direction and given their <paramref name="cells"/> in reading order (nearest first, across each pair from the left chest to the right one).
        /// </summary>
        private static PlannedPlatform PlacePlatform(World world, BuildBeacon beacon, BuildTemplate template, int turn, int index, double fx, double fy, double fz,
            Func<string, bool> isBuilding, IReadOnlyList<Cell> cells, string? group = null, int row = 0)
        {
            var half = template.Spacing / 2;

            var exists = world.Objects.Any(o => o.GId == "Foundation" && Math.Abs(o.X - fx) < 0.6 && Math.Abs(o.Z - fz) < 0.6 && Math.Abs(o.Y - fy) < 0.6);

            var conflicts = world.Objects
                .Where(o => o.Id != beacon.Id && isBuilding(o.GId)
                            && Math.Abs(o.X - fx) < half - 0.05 && Math.Abs(o.Z - fz) < half - 0.05
                            && o.Y > fy - 1 && o.Y < fy + 8
                            && !(o.GId == "Foundation" && Math.Abs(o.X - fx) < 0.6 && Math.Abs(o.Z - fz) < 0.6 && Math.Abs(o.Y - fy) < 0.6))
                .Take(4)
                .Select(o => $"{o.GId} at ({o.X:0.#}, {o.Z:0.#})")
                .ToList();

            // Where each chest ends up: the template offset turned to this row's direction, and the chest turned with it.
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
                .ThenByDescending(p => Math.Round(-p.Ox * beacon.DirZ + p.Oz * beacon.DirX, 1))
                .ToList();

            var chests = new List<PlannedChest>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i];
                var (px, py, pz) = (Math.Round(fx + p.Ox, 3), Math.Round(fy + p.Dy, 3), Math.Round(fz + p.Oz, 3));
                var cell = i < cells.Count ? cells[i] : default;

                chests.Add(cell.Bundle is { } bundle
                    ? new PlannedChest(px, py, pz, p.Rot, null, bundle.Title, bundle.Items, bundle.FillWith)
                    : new PlannedChest(px, py, pz, p.Rot, cell.Label));
            }

            return new PlannedPlatform(index, fx, fy, fz, exists, chests, conflicts, group, row);
        }

        private static BuildPlan PlanCore(string text, long beaconId, BuildTemplate template, IReadOnlyList<string> items, IReadOnlyList<BuildBundle>? bundles, Func<string, bool> isBuilding)
        {
            var world = Read(text);
            var beacon = Beacons(world).FirstOrDefault(b => b.Id == beaconId);
            var problems = new List<string>();

            if (bundles is not null)
            {
                bundles = PrepareBundles(text, template, bundles);
                items = bundles.Select(b => b.Title).ToList();
            }

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

            var turn = TurnFor(template, beacon);
            var count = (int)Math.Ceiling(items.Count / (double)perPlatform);
            var platforms = new List<PlannedPlatform>();

            for (var k = 1; k <= count; k++)
            {
                var (fx, fy, fz) = (beacon.FoundationX + k * template.Spacing * beacon.DirX, beacon.FoundationY, beacon.FoundationZ + k * template.Spacing * beacon.DirZ);

                var cells = Enumerable.Range((k - 1) * perPlatform, perPlatform).Select(n =>
                    bundles is not null
                        ? new Cell(null, n < bundles.Count ? bundles[n] : null)
                        : ItemCell(n < items.Count ? items[n] : null)).ToList();

                platforms.Add(PlacePlatform(world, beacon, template, turn, k, fx, fy, fz, isBuilding, cells));
            }

            return new BuildPlan(beacon, template, items, platforms, problems);
        }

        /// <summary>
        /// One group of a warehouse: a name, and either the items its chests are labelled with or the stocked bundles it holds (the equipment group).
        /// An item written "=Name" is a holding tank: a chest with that title and no item filter, demand or contents.
        /// </summary>
        public sealed record WarehouseGroup(string Name, IReadOnlyList<string> Items, IReadOnlyList<BuildBundle>? Bundles = null);

        /// <summary>A sign of a warehouse row's index: the text it says, and whether it hangs on the row's right chest (else the left one). Signs are stacked top to bottom in the order given.</summary>
        public sealed record WarehouseSign(bool Right, string Text);

        /// <summary>One row of a warehouse: its groups and the index signs for its first platform. A blank row is an aisle of bare foundations.</summary>
        public sealed record WarehouseRow(IReadOnlyList<WarehouseGroup> Groups, IReadOnlyList<WarehouseSign> Signs, bool Blank = false);

        // Where a sign hangs on a chest, measured from the owner's own signs in Custom-2 (2026-09-27) for a beacon pointing south: 0.76 m in front of the chest (toward the
        // beacon), the top sign 2.745 m above the chest's base and the rest stacked 0.575 m apart below it, all with the identity rotation.
        private const double SignOffset = 0.76, SignTop = 2.745, SignPitch = 0.575;

        /// <summary>
        /// A plan for a whole warehouse from one beacon. <paramref name="rows"/> is a list of rows. The first row runs out from the platform the beacon points
        /// at, the next row beside it on the beacon's right hand (a beacon pointing south puts it to the west), and so on. In a row every group starts on a platform
        /// of its own, so a group's chests are never on a platform shared with another group. Rows shorter than the longest are finished with bare foundations,
        /// so the warehouse is a full rectangle; a blank row is all bare foundations. Blank rows before the first row with groups lie on the beacon's left hand
        /// (a beacon pointing south puts one to the east). The first platform of a row carries its index signs. Only buildings are checked for room; the ground
        /// is not known.
        /// </summary>
        public static BuildPlan PlanWarehouse(string text, long beaconId, BuildTemplate template, IReadOnlyList<WarehouseRow> rows, Func<string, bool> isBuilding, int minWidth = 0)
        {
            var world = Read(text);
            var beacon = Beacons(world).FirstOrDefault(b => b.Id == beaconId);
            var problems = new List<string>();

            if (beacon is null)
                return new BuildPlan(new BuildBeacon(beaconId, "", 0, 0, 0, 0, 0, 0, null, 0, 0, 0), template, Array.Empty<string>(), Array.Empty<PlannedPlatform>(), new[] { "That beacon is not in the save." });

            if (beacon.FoundationId is null)
                problems.Add("The beacon is not standing on a foundation.");

            var perPlatform = template.Chests.Count;
            if (perPlatform == 0)
                problems.Add("The template has no chests.");

            // The cells of every group, row by row (a blank row has no groups).
            var laid = new List<(bool Blank, IReadOnlyList<WarehouseSign> Signs, List<(string Name, List<Cell> Cells)> Groups)>();
            foreach (var row in rows)
            {
                var groups = new List<(string Name, List<Cell> Cells)>();
                foreach (var g in row.Groups)
                {
                    List<Cell> cells;
                    if (g.Bundles is not null)
                    {
                        var prepared = PrepareBundles(text, template, g.Bundles);
                        var biggest = prepared.OrderByDescending(b => b.Items.Count).FirstOrDefault();
                        if (biggest is not null && biggest.Items.Count > template.Slots)
                            problems.Add($"\"{biggest.Title}\" has {biggest.Items.Count} items but the {template.Name} template's chests hold {template.Slots}. Use a template with more slots (Container2 or Container3).");

                        cells = prepared.Select(b => new Cell(null, b)).ToList();
                    }
                    else
                        cells = g.Items.Select(i => ItemCell(i)).ToList();

                    if (cells.Count > 0)
                        groups.Add((g.Name, cells));
                }

                if (row.Blank || groups.Count > 0)
                    laid.Add((row.Blank, row.Signs, groups));
            }

            if (!laid.Any(r => r.Groups.Count > 0))
                problems.Add("There is nothing to build for.");

            var allItems = laid.SelectMany(r => r.Groups).SelectMany(g => g.Cells).Select(c => c.Label ?? c.Bundle!.Title).ToList();

            if (problems.Count > 0)
                return new BuildPlan(beacon, template, allItems, Array.Empty<PlannedPlatform>(), problems.Distinct().ToList());

            var turn = TurnFor(template, beacon);
            var (dx, dz) = (beacon.DirX, beacon.DirZ);
            var (rx, rz) = (dz, -dx); // the right hand of the beacon: for south (-x) it is +z, the west side

            // Signs are turned like the chests: identity when the beacon points south, the same turn from south for another direction.
            int c0 = -dx, s0 = -dz;
            var signTurn = (int)Math.Round(-Math.Atan2(s0, c0) * 180 / Math.PI);
            var signRot = TurnRot("0,0,0,1", signTurn);

            // Each row's platforms in order, then bare foundations up to the longest row.
            var rowPlatforms = laid.Select(row => row.Groups.SelectMany(g =>
                Enumerable.Range(0, (int)Math.Ceiling(g.Cells.Count / (double)perPlatform))
                    .Select(p => (g.Name, Cells: g.Cells.Skip(p * perPlatform).Take(perPlatform).ToList()))).ToList()).ToList();
            // One extra platform at the back of every row (the owner's 2026-09-27 "11 deep" change), beyond whatever the groups need.
            var width = Math.Max(rowPlatforms.Max(r => r.Count), minWidth);
            var leadingBlank = laid.TakeWhile(r => r.Blank).Count();

            var platforms = new List<PlannedPlatform>();
            var index = 0;
            for (var r = 0; r < rowPlatforms.Count; r++)
            {
                for (var c = 0; c < width; c++)
                {
                    index++;
                    var along = (c + 1) * template.Spacing;
                    var across = (r - leadingBlank) * template.Spacing;
                    var (fx, fy, fz) = (beacon.FoundationX + along * dx + across * rx, beacon.FoundationY, beacon.FoundationZ + along * dz + across * rz);

                    if (c >= rowPlatforms[r].Count)
                    {
                        platforms.Add(PlaceBare(world, beacon, template, index, fx, fy, fz, isBuilding, r + 1, laid[r].Blank ? "(blank)" : "(filler)"));
                        continue;
                    }

                    var platform = PlacePlatform(world, beacon, template, turn, index, fx, fy, fz, isBuilding, rowPlatforms[r][c].Cells, rowPlatforms[r][c].Name, r + 1);

                    // The first platform of a row carries the row's index: the left signs on its nearest left chest, the right ones on its nearest right chest.
                    if (c == 0 && laid[r].Signs.Count > 0 && platform.Chests.Count >= 2)
                    {
                        var signs = new List<PlannedSign>();
                        foreach (var side in new[] { false, true })
                        {
                            var stack = laid[r].Signs.Where(s => s.Right == side).ToList();
                            var chest = platform.Chests[side ? 1 : 0];
                            for (var i = 0; i < stack.Count; i++)
                                signs.Add(new PlannedSign(Math.Round(chest.X - SignOffset * dx, 3), Math.Round(chest.Y + SignTop - i * SignPitch, 3), Math.Round(chest.Z - SignOffset * dz, 3), signRot, stack[i].Text));
                        }

                        platform = platform with { Signs = signs };
                    }

                    platforms.Add(platform);
                }
            }

            return new BuildPlan(beacon, template, allItems, platforms, problems);
        }

        /// <summary>What removing a warehouse would delete, found from the platforms of its plan. Nothing is deleted by asking.</summary>
        /// <param name="Blockers">Things that stand in the footprint but are not a foundation or a chest (a machine, a wall): they are not ours to delete, so any of them stops a removal.</param>
        public sealed record RemovalPlan(int Platforms, int Foundations, int Chests, int Items, IReadOnlyList<string> Blockers, IReadOnlyList<long> ObjectIds, IReadOnlyList<long> InventoryIds, int Signs = 0, int Crafters = 0)
        {
            public bool Any => Foundations > 0 || Chests > 0;
        }

        public sealed record RemovalOutcome(string? NewText, RemovalPlan Plan, IReadOnlyList<string> Problems)
        {
            public bool Failed => Problems.Count > 0;
        }

        private sealed record Rec(int Start, int Length, long Id, bool IsInventory, string[] WoIds, bool HasInventory);

        private static List<Rec> ReadRecords(string text)
        {
            var result = new List<Rec>();
            foreach (Match match in RecordPattern.Matches(text))
            {
                try
                {
                    using var doc = JsonDocument.Parse(match.Value);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
                        continue;

                    var isInventory = root.TryGetProperty("woIds", out var wo);
                    var ids = isInventory && wo.ValueKind == JsonValueKind.String
                        ? (wo.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        : Array.Empty<string>();
                    result.Add(new Rec(match.Index, match.Length, id, isInventory, ids, root.TryGetProperty("liId", out _)));
                }
                catch (JsonException)
                {
                    // Not a record we can read; leave it be.
                }
            }

            return result;
        }

        /// <summary>
        /// Finds a built warehouse from the platforms of its plan (<see cref="PlanWarehouse"/>): the foundations standing there, the chests on them
        /// (any Container), each chest's inventory and every item in it. Anything else standing on a platform is listed as a blocker.
        /// </summary>
        public static RemovalPlan PlanRemoval(string text, BuildPlan plan, Func<string, bool> isBuilding, bool includeCrafters = false)
        {
            var world = Read(text);
            var records = ReadRecords(text);
            var inventories = records.Where(r => r.IsInventory).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
            var withInventory = records.Where(r => !r.IsInventory && r.HasInventory).Select(r => r.Id).ToHashSet();
            var half = plan.Template.Spacing / 2;

            var objects = new HashSet<long>();
            var invIds = new HashSet<long>();
            var blockers = new List<string>();
            int foundations = 0, chests = 0, items = 0, platforms = 0, signs = 0, crafters = 0;

            foreach (var p in plan.Platforms)
            {
                // A factory floor may have been laid by hand on a grid a little off the warehouse's (the owner's is 1 m off), so any foundation of that height whose
                // centre is in the platform's square is part of it; for a warehouse only one exactly on its platform is.
                var reach = includeCrafters ? half - 0.05 : 0.6;
                // A factory floor may carry a ramp (FoundationSlope, at some height between the warehouse and the floor, in place of the usual flat tile): removing the factory
                // takes those with it too, over a generous band below the floor, since a ramp is only ever built on a factory floor (never the warehouse, which stays flat).
                var found = world.Objects.Where(o => Math.Abs(o.X - p.X) < reach && Math.Abs(o.Z - p.Z) < reach
                                                     && (o.GId == "Foundation" && Math.Abs(o.Y - p.Y) < 0.6
                                                         || (includeCrafters && o.GId == "FoundationSlope" && o.Y > p.Y - 12 && o.Y < p.Y + 1))).ToList();
                var boxes = world.Objects.Where(o => (o.GId.StartsWith("Container", StringComparison.Ordinal) || (includeCrafters && o.GId.StartsWith("AutoCrafter", StringComparison.Ordinal)))
                                                     && Math.Abs(o.X - p.X) <= half && Math.Abs(o.Z - p.Z) <= half
                                                     && Math.Abs(o.Y - p.Y - OnFoundation) < 0.8).ToList();
                if (found.Count > 0 || boxes.Count > 0)
                    platforms++;

                foreach (var f in found)
                    if (objects.Add(f.Id)) foundations++;

                foreach (var c in boxes)
                {
                    if (!objects.Add(c.Id))
                        continue;

                    chests++;
                    if (c.GId.StartsWith("AutoCrafter", StringComparison.Ordinal))
                        crafters++;

                    if (c.LiId is not { } li)
                        continue;

                    if (!inventories.TryGetValue(li, out var inv))
                    {
                        blockers.Add($"{c.GId} at ({c.X:0.#}, {c.Z:0.#}) points at inventory {li}, which is not in the save");
                        continue;
                    }

                    invIds.Add(li);
                    foreach (var raw in inv.WoIds)
                    {
                        if (!long.TryParse(raw, out var item))
                            continue;

                        if (withInventory.Contains(item))
                            blockers.Add($"item {item} in {c.GId} at ({c.X:0.#}, {c.Z:0.#}) has an inventory of its own");
                        else if (objects.Add(item))
                            items++;
                    }
                }

                // The signs hung on the chests (the row index) go with them: near a chest (within 1.3 m across) and above its base.
                var signsHere = boxes.SelectMany(b => world.Objects.Where(o => o.GId == "Sign"
                                                                              && Math.Sqrt(Math.Pow(o.X - b.X, 2) + Math.Pow(o.Z - b.Z, 2)) < 1.3
                                                                              && o.Y > b.Y + 0.5 && o.Y < b.Y + 3.3)).ToList();
                foreach (var s in signsHere)
                    if (objects.Add(s.Id)) signs++;

                var removedHere = new HashSet<long>(found.Select(f => f.Id).Concat(boxes.Select(b => b.Id)).Concat(signsHere.Select(s => s.Id)));

                // Only where part of the warehouse stands: a platform with nothing of ours on it (an aisle not built yet) is not ours to judge.
                if (found.Count == 0 && boxes.Count == 0)
                    continue;

                foreach (var o in world.Objects.Where(o => isBuilding(o.GId) && !removedHere.Contains(o.Id)
                                                           && Math.Abs(o.X - p.X) < half - 0.05 && Math.Abs(o.Z - p.Z) < half - 0.05
                                                           && o.Y > p.Y - 1 && o.Y < p.Y + 8).Take(3))
                    blockers.Add($"{o.GId} at ({o.X:0.#}, {o.Z:0.#})");
            }

            return new RemovalPlan(platforms, foundations, chests, items, blockers.Distinct().Take(6).ToList(), objects.ToList(), invIds.ToList(), signs, crafters);
        }

        /// <summary>
        /// Takes a built warehouse out of the text of a save: its foundations, chests, their inventories and the items in them, and nothing else. A blocker
        /// (something else in the footprint) means nothing is removed. The result is checked against the original before anyone may write it: the records
        /// that remain must be exactly the original ones in the same order minus the removed ones, and the text between them must be as long as it was
        /// less one separator for each record taken out.
        /// </summary>
        public static RemovalOutcome Remove(string text, BuildPlan plan, Func<string, bool> isBuilding, bool includeCrafters = false)
        {
            var removal = PlanRemoval(text, plan, isBuilding, includeCrafters);

            if (plan.Problems.Count > 0)
                return new RemovalOutcome(null, removal, plan.Problems.ToList());

            if (!removal.Any)
                return new RemovalOutcome(null, removal, new[] { "There is no warehouse here to remove." });

            if (removal.Blockers.Count > 0)
                return new RemovalOutcome(null, removal, new[] { "Something else stands in the warehouse (" + string.Join("; ", removal.Blockers) + "), so nothing was removed. Take it away first." });

            var (newText, problems) = RemoveByIds(text, removal.ObjectIds, removal.InventoryIds);
            return problems.Count > 0 ? new RemovalOutcome(null, removal, problems) : new RemovalOutcome(newText, removal, Array.Empty<string>());
        }

        /// <summary>
        /// Takes exactly the given object and inventory ids out of the text (one record each; a record goes with the separator after it, or before it when it is the last of
        /// a section), and checks the result before returning it: the records that remain are the original ones minus these, in order, and only separators were lost from the
        /// text between them. Used by every "remove" (a whole warehouse or factory, or one crafter).
        /// </summary>
        private static (string? NewText, List<string> Problems) RemoveByIds(string text, IReadOnlyList<long> objectIds, IReadOnlyList<long> inventoryIds)
        {
            var objectSet = objectIds.ToHashSet();
            var inventorySet = inventoryIds.ToHashSet();
            var records = ReadRecords(text);
            var doomed = records.Where(r => r.IsInventory ? inventorySet.Contains(r.Id) : objectSet.Contains(r.Id)).OrderBy(r => r.Start).ToList();

            if (doomed.Count(r => !r.IsInventory) != objectSet.Count || doomed.Count(r => r.IsInventory) != inventorySet.Count)
                return (null, new List<string> { "The records found in the save do not match what is being removed (an id is missing or appears twice), so nothing was removed." });

            var eol = text.Contains("|\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var sep = "|" + eol;
            var result = new System.Text.StringBuilder(text);

            // A record goes with the separator after it; the last of a section, which has none, takes the one before it. Two neighbours can claim the
            // same separator, so the spans are merged before anything is cut, and cut from the end so earlier positions stay valid.
            var spans = new List<(int Start, int End)>();
            foreach (var r in doomed)
            {
                var (start, end) = (r.Start, r.Start + r.Length);
                if (string.CompareOrdinal(text, end, sep, 0, sep.Length) == 0)
                    end += sep.Length;
                else if (start >= sep.Length && string.CompareOrdinal(text, start - sep.Length, sep, 0, sep.Length) == 0)
                    start -= sep.Length;

                if (spans.Count > 0 && start <= spans[^1].End)
                    spans[^1] = (spans[^1].Start, Math.Max(spans[^1].End, end));
                else
                    spans.Add((start, end));
            }

            for (var i = spans.Count - 1; i >= 0; i--)
                result.Remove(spans[i].Start, spans[i].End - spans[i].Start);

            var newText = result.ToString();
            var problems = VerifyRemoval(text, newText, doomed.Count, sep, doomed);
            return problems.Count > 0 ? (null, problems) : (newText, new List<string>());
        }

        private static List<string> VerifyRemoval(string before, string after, int removed, string sep, List<Rec> doomed)
        {
            var problems = new List<string>();

            var skip = new HashSet<int>(doomed.Select(d => d.Start));
            var expected = RecordPattern.Matches(before).Where(m => !skip.Contains(m.Index)).Select(m => m.Value).ToList();
            var actual = RecordPattern.Matches(after).Select(m => m.Value).ToList();

            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                problems.Add("The records left after the removal are not exactly the original ones minus the warehouse, so nothing was written.");

            // Between the records only separators may have gone (at most one for each record removed): the rest of that text, section marks included, is untouched.
            var (gapBefore, gapAfter) = (RecordPattern.Replace(before, ""), RecordPattern.Replace(after, ""));
            var gone = gapBefore.Length - gapAfter.Length;
            if (gone < 0 || gone % sep.Length != 0 || gone / sep.Length > removed || gapBefore.Replace(sep, "") != gapAfter.Replace(sep, ""))
                problems.Add("The text between the records changed by more than the separators of the removed records, so nothing was written.");

            return problems;
        }

        /// <summary>A platform with nothing on it: a foundation only (or nothing to write, if one already stands there).</summary>
        private static PlannedPlatform PlaceBare(World world, BuildBeacon beacon, BuildTemplate template, int index, double fx, double fy, double fz, Func<string, bool> isBuilding, int row, string group)
        {
            var full = PlacePlatform(world, beacon, template, 0, index, fx, fy, fz, isBuilding, Array.Empty<Cell>(), group, row);
            return full with { Chests = Array.Empty<PlannedChest>() };
        }
    }
}
