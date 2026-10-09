using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>What a machine's or crate's inventory looked like in the captured base (see tools/capture-main-base.py).</summary>
    public sealed class MainBaseInventory
    {
        public int Size { get; set; }

        /// <summary>The items it demands, comma separated. Null when the inventory has no drone settings at all.</summary>
        public string? Demand { get; set; }

        /// <summary>The items it supplies, comma separated; "*" is everything (the save's own list, or the dashboard's, filled in at build time).</summary>
        public string? Supply { get; set; }

        public int? Priority { get; set; }

        /// <summary>What it held when captured (by item, with counts). Only built when the build is asked to copy contents.</summary>
        public Dictionary<string, int>? Held { get; set; }

        /// <summary>What it is filled with when the build is asked to fill the drone stations (they hold T3 drones).</summary>
        public Dictionary<string, int>? Fill { get; set; }

        /// <summary>What it is filled with when the build is asked to stock the disposal crates (the ore each one is set to demand, filling it).</summary>
        public Dictionary<string, int>? Stock { get; set; }

        /// <summary>What it always holds: an optimizer's fuses are its setup, not a product.</summary>
        public Dictionary<string, int>? Keep { get; set; }
    }

    /// <summary>One object of the captured base: where it stands from the beacon's own foundation, and everything the save wrote about it.</summary>
    public sealed class MainBaseObject
    {
        public string G { get; set; } = "";
        public double Dx { get; set; }
        public double Dy { get; set; }
        public double Dz { get; set; }
        public string Rot { get; set; } = "0,0,0,1";
        public string? Pnls { get; set; }
        public string? Color { get; set; }
        public string? Text { get; set; }
        public string? LiGrps { get; set; }
        public MainBaseInventory? Inv { get; set; }

        /// <summary>The sizes of its secondary inventories (farms and growers keep their plants there).</summary>
        public List<int>? Sec { get; set; }
    }

    public sealed class MainBaseBeacon
    {
        public string Text { get; set; } = "";
        public int DirX { get; set; }
        public int DirZ { get; set; }
    }

    public sealed class MainBaseTemplate
    {
        public int Schema { get; set; }
        public string Name { get; set; } = "";
        public string? CapturedFrom { get; set; }
        public string? Excluded { get; set; }
        public MainBaseBeacon Beacon { get; set; } = new();
        public Dictionary<string, int> Counts { get; set; } = new();
        public List<MainBaseObject> Objects { get; set; } = new();
    }

    /// <summary>One object of a plan: the template's object placed at a world position, turned to the beacon's direction.</summary>
    public sealed record PlacedObject(MainBaseObject Source, double X, double Y, double Z, string Rot, bool Exists);

    /// <summary>A piece standing in the save that belongs to another tier's base but not to the one being built: an upgrade takes it away (with what is in it) before it builds.</summary>
    public sealed record ObsoleteObject(long Id, string G, double X, double Y, double Z);

    public sealed record MainBasePlan(BuildBeacon? Beacon, int Turn, IReadOnlyList<PlacedObject> Objects, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Problems,
        IReadOnlyList<ObsoleteObject>? Obsolete = null)
    {
        public IReadOnlyList<ObsoleteObject> ObsoleteObjects => Obsolete ?? Array.Empty<ObsoleteObject>();

        public int NewCount => Objects.Count(o => !o.Exists);
        public int ExistingCount => Objects.Count(o => o.Exists);
        public bool Ok => Beacon is not null && Problems.Count == 0 && Conflicts.Count == 0;
    }

    public sealed record MainBaseOutcome(string? NewText, int Objects, int Inventories, int Items, IReadOnlyList<string> Problems, int Removed = 0)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>
    /// The Base Building "Main Base" template: the owner's finished base (captured from the save he built it in, see tools/capture-main-base.py) built from
    /// any beacon named <c>Base...</c>, turned to the way that beacon points. Every object is written fresh, with new ids and its own new inventories (empty,
    /// with the captured drone settings, and the drone stations filled); nothing already in the save is changed. Heights follow the beacon's own foundation,
    /// and only buildings are checked for room, not the ground.
    /// </summary>
    public static partial class BaseBuildingEngine
    {
        public static readonly JsonSerializerOptions TemplateJson = new() { PropertyNameCaseInsensitive = true };

        public const string SupplyEverythingMarker = "*";

        /// <summary>
        /// The second kind of anchor for the Main Base, for the start of a game, before beacons are unlocked: an outdoor lamp standing on a foundation. Like the
        /// beacon it gives the foundation to build from (the one under it) and the direction (its facing), and it is left exactly as it is.
        /// </summary>
        public const string AnchorLampGId = "OutsideLamp1";

        /// <summary>
        /// Where a Main Base can be built from: every beacon named <c>Base...</c> that stands on a foundation, and every outdoor lamp that stands on one (the
        /// nearest foundation to it, within its own 6 m square). The record's text is "Outdoor lamp" for a lamp.
        /// </summary>
        public static IReadOnlyList<BuildBeacon> FindMainBaseAnchors(string text) => MainBaseAnchors(Read(text));

        private static List<BuildBeacon> MainBaseAnchors(World world)
        {
            var result = Beacons(world).Where(b => b.Text.StartsWith("Base", StringComparison.OrdinalIgnoreCase)).ToList();
            var foundations = world.Objects.Where(o => o.GId == "Foundation").ToList();

            foreach (var lamp in world.Objects.Where(o => o.GId == AnchorLampGId))
            {
                var under = foundations
                    .Where(f => Math.Abs(f.X - lamp.X) <= PlatformSpacing / 2 && Math.Abs(f.Z - lamp.Z) <= PlatformSpacing / 2 && lamp.Y - f.Y is > 1 and < 4)
                    .OrderBy(f => Math.Abs(f.X - lamp.X) + Math.Abs(f.Z - lamp.Z))
                    .FirstOrDefault();

                if (under is null)
                    continue;

                var yaw = YawOf(lamp.Rot);
                var (dx, dz) = Snap(yaw);
                result.Add(new BuildBeacon(lamp.Id, "Outdoor lamp", lamp.X, lamp.Y, lamp.Z, yaw, dx, dz, under.Id, under.X, under.Y, under.Z, lamp.Planet));
            }

            return result.OrderBy(b => b.Text, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The turn that carries the captured direction to the way this anchor points (positive turns carry +z toward +x, as the platform templates do), with its cosine and sine.</summary>
        private static (int Turn, double Cos, double Sin) TurnFor(MainBaseTemplate template, BuildBeacon beacon)
        {
            int c0 = template.Beacon.DirX * beacon.DirX + template.Beacon.DirZ * beacon.DirZ;
            int s0 = template.Beacon.DirX * beacon.DirZ - template.Beacon.DirZ * beacon.DirX;
            var turn = (int)Math.Round(-Math.Atan2(s0, c0) * 180 / Math.PI);
            return (turn, Math.Round(Math.Cos(turn * Math.PI / 180)), Math.Round(Math.Sin(turn * Math.PI / 180)));
        }

        /// <summary>How close an existing object's position may be to a new one's before the two are said to be in each other's way.</summary>
        private const double SamePlace = 0.6;

        /// <summary>How close counts as "exactly there" when telling one tier's piece from another's (the captures are written to five decimals, so a piece built from a template is within a hair of it).</summary>
        private const double ExactPlace = 0.05;

        /// <param name="others">
        /// The other tiers' templates, for an upgrade: a piece in the save that one of them places (same kind, same spot) but the template being built does not is obsolete, and goes
        /// before the new pieces are built. Only pieces a template placed are ever taken: anything else standing there (the owner's own) is left, and blocks the build if it is in the way.
        /// </param>
        public static MainBasePlan PlanMainBase(string text, long beaconId, MainBaseTemplate template, Func<string, bool> isBuilding, IReadOnlyList<MainBaseTemplate>? others = null)
        {
            MainBasePlan Problem(string message) => new(null, 0, Array.Empty<PlacedObject>(), Array.Empty<string>(), new[] { message });

            var world = Read(text);
            var beacon = MainBaseAnchors(world).FirstOrDefault(b => b.Id == beaconId);
            if (beacon is null)
                return Problem("That beacon or lamp is not in the save.");

            if (beacon.FoundationId is null)
                return Problem("It is not standing on a foundation.");

            var (turn, cs, sn) = TurnFor(template, beacon);

            var placed = new List<PlacedObject>(template.Objects.Count);
            var conflicts = new List<string>();

            // Where each piece a tier places lands from this anchor.
            (double X, double Y, double Z) Place(MainBaseObject t) =>
                (Math.Round(beacon.FoundationX + t.Dx * cs + t.Dz * sn, 5), Math.Round(beacon.FoundationY + t.Dy, 5), Math.Round(beacon.FoundationZ + (-t.Dx * sn + t.Dz * cs), 5));

            var byKind = world.Objects.GroupBy(o => o.GId).ToDictionary(g => g.Key, g => g.ToList());
            IEnumerable<Obj> Standing(string g, double x, double y, double z, double within = SamePlace) =>
                byKind.TryGetValue(g, out var list)
                    ? list.Where(o => Math.Abs(o.X - x) < within && Math.Abs(o.Z - z) < within && Math.Abs(o.Y - y) < within)
                    : Enumerable.Empty<Obj>();

            // What the template being built already has standing exactly where it puts it, and, for an upgrade, what the other tiers have standing exactly where they put it that
            // this one does not: those go, and this one's own piece is built where it wants it. A piece the owner moved by hand matches neither exactly, so it stays.
            var wanted = new HashSet<long>();
            var scheduled = new Dictionary<long, ObsoleteObject>();
            foreach (var t in template.Objects)
            {
                var (x, y, z) = Place(t);
                foreach (var o in Standing(t.G, x, y, z, ExactPlace))
                    wanted.Add(o.Id);
            }

            if (others is not null)
            {
                foreach (var other in others)
                {
                    foreach (var t in other.Objects)
                    {
                        var (x, y, z) = Place(t);
                        foreach (var o in Standing(t.G, x, y, z, ExactPlace))
                        {
                            if (wanted.Contains(o.Id) || o.Id == beacon.Id || o.Id == beacon.FoundationId || o.GId == "EscapePod" || scheduled.ContainsKey(o.Id))
                                continue;

                            scheduled[o.Id] = new ObsoleteObject(o.Id, o.GId, o.X, o.Y, o.Z);
                        }
                    }
                }
            }

            foreach (var t in template.Objects)
            {
                var (x, y, z) = Place(t);

                // Something of the same kind already stands exactly there: this part of the base is already built (a foundation under the beacon always is).
                var twin = Standing(t.G, x, y, z).Any(o => !scheduled.ContainsKey(o.Id));

                if (!twin && conflicts.Count < 4)
                {
                    // A different building in the same spot would be built through (unless it is a piece of another tier that is being taken away).
                    var other = world.Objects.FirstOrDefault(o => o.Id != beacon.Id && o.GId != t.G && isBuilding(o.GId) && !scheduled.ContainsKey(o.Id)
                                                                  && Math.Abs(o.X - x) < SamePlace && Math.Abs(o.Z - z) < SamePlace && Math.Abs(o.Y - y) < SamePlace);
                    if (other is not null)
                        conflicts.Add($"{other.GId} at ({other.X:0.#}, {other.Z:0.#}) is where a {t.G} goes");
                }

                placed.Add(new PlacedObject(t, x, y, z, TurnRot(t.Rot, turn), twin));
            }

            return new MainBasePlan(beacon, turn, placed, conflicts, Array.Empty<string>(), scheduled.Values.ToList());
        }

        /// <param name="prefill">Pre-fill the base: the drone stations get their drones, the optimizers their fuses and the disposal-room ore crates their ore. Everything else (suppliers, machines) starts empty and fills up on its own.</param>
        /// <param name="copyContents">Also put back what each machine held when the base was captured (honey, gas capsules...). Not offered on the page.</param>
        public static MainBaseOutcome ApplyMainBase(string text, MainBasePlan plan, bool prefill, bool copyContents = false, Random? random = null)
        {
            random ??= Rng;

            MainBaseOutcome Fail(IReadOnlyList<string> problems) => new(null, 0, 0, 0, problems);

            if (plan.Beacon is null || plan.Problems.Count > 0)
                return Fail(plan.Problems.Count > 0 ? plan.Problems : new[] { "There is no beacon to build from." });

            if (plan.Conflicts.Count > 0)
                return Fail(new[] { $"Something is in the way ({string.Join("; ", plan.Conflicts)}), so nothing was built." });

            // An upgrade: the pieces of another tier that this one does not have go first, with their inventories and what is in them. The records that remain are checked
            // to be exactly the original ones minus these (RemoveByIds), and the build after that is checked against the text as it is then.
            var removedCount = 0;
            if (plan.ObsoleteObjects.Count > 0)
            {
                var (objectsOut, inventoriesOut, _) = CollectWithInventories(text, plan.ObsoleteObjects.Select(o => o.Id));
                var (cleared, clearProblems) = RemoveByIds(text, objectsOut.ToList(), inventoriesOut.ToList());
                if (cleared is null)
                    return Fail(clearProblems);

                text = cleared;
                removedCount = plan.ObsoleteObjects.Count;
            }

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
                    return Fail(new[] { $"A record at position {match.Index} is not valid JSON: {ex.Message}" });
                }
            }

            var beacon = records.FirstOrDefault(r => !r.IsInventory && r.Id == plan.Beacon.Id
                                                      && (r.Raw.Contains("\"gId\":\"Beacon\"", StringComparison.Ordinal) || r.Raw.Contains("\"gId\":\"" + AnchorLampGId + "\"", StringComparison.Ordinal)));
            var inventories = records.Where(r => r.IsInventory).ToList();
            if (beacon.Raw is null || inventories.Count == 0)
                return Fail(new[] { "The beacon or lamp (or the save's inventories) could not be found in the text, so nothing was built." });

            var planet = Planet.Match(beacon.Raw).Groups[1].Value;
            if (planet.Length == 0)
                return Fail(new[] { "The beacon or lamp has no planet, so nothing was built." });

            var everything = LearnEverythingFrom(inventories.Select(r => r.Raw));
            var used = new HashSet<long>(records.Select(r => r.Id));
            var nextInventory = inventories.Max(r => r.Id);
            var eol = text.Contains("|\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            long NewObjectId()
            {
                while (true)
                {
                    var id = random.NextInt64(ObjectIdMin, ObjectIdMax);
                    if (used.Add(id))
                        return id;
                }
            }

            long NewInventoryId()
            {
                do { nextInventory++; } while (!used.Add(nextInventory));
                return nextInventory;
            }

            // Five decimals: the save writes heights like 34.53583, and a base captured from it should come back the same.
            string Pos(double x, double y, double z) => string.Join(",", new[] { x, y, z }.Select(v => Math.Round(v, 5).ToString("0.#####", CultureInfo.InvariantCulture)));

            var newObjects = new List<string>();
            var newInventories = new List<string>();
            var objectIds = new List<long>();
            var inventoryIds = new List<long>();
            var links = new List<long>();
            var objectCount = 0;
            var items = 0;

            // An inventory record, with whatever it holds written as new item records of their own.
            long AddInventory(MainBaseInventory? spec, int size, bool withSettings)
            {
                var id = NewInventoryId();
                inventoryIds.Add(id);
                links.Add(id);

                var held = new List<long>();
                var wanted = new List<(string GId, int Count)>();
                if (spec?.Keep is { } keep && prefill)
                    wanted.AddRange(keep.Select(p => (p.Key, p.Value)));
                if (spec?.Fill is { } fill && prefill)
                    wanted.AddRange(fill.Select(p => (p.Key, p.Value)));
                if (spec?.Stock is { } stock && prefill)
                    wanted.AddRange(stock.Select(p => (p.Key, p.Value)));
                if (spec?.Held is { } contents && copyContents)
                    wanted.AddRange(contents.Select(p => (p.Key, p.Value)));

                foreach (var (gId, count) in wanted)
                {
                    for (var n = 0; n < count && held.Count < size; n++)
                    {
                        var itemId = NewObjectId();
                        objectIds.Add(itemId);
                        held.Add(itemId);
                        newObjects.Add($"{{\"id\":{itemId},\"gId\":{Q(gId)}}}");
                    }
                }

                items += held.Count;

                var settings = "";
                if (withSettings)
                {
                    var supply = spec?.Supply == SupplyEverythingMarker ? string.Join(",", everything) : spec?.Supply ?? "";
                    settings = $",\"demandGrps\":{Q(spec?.Demand ?? "")},\"supplyGrps\":{Q(supply)},\"priority\":{(spec?.Priority ?? 0).ToString(CultureInfo.InvariantCulture)}";
                }

                newInventories.Add($"{{\"id\":{id},\"woIds\":\"{string.Join(",", held)}\",\"size\":{size.ToString(CultureInfo.InvariantCulture)}{settings}}}");
                return id;
            }

            foreach (var p in plan.Objects.Where(o => !o.Exists))
            {
                var t = p.Source;
                var id = NewObjectId();
                objectIds.Add(id);
                objectCount++;

                var sb = new StringBuilder();
                sb.Append("{\"id\":").Append(id).Append(",\"gId\":").Append(Q(t.G));

                if (t.Inv is { } inv)
                    sb.Append(",\"liId\":").Append(AddInventory(inv, inv.Size, inv.Demand is not null));

                if (t.LiGrps is not null)
                    sb.Append(",\"liGrps\":").Append(Q(t.LiGrps));

                if (t.Sec is { Count: > 0 } sec)
                    sb.Append(",\"siIds\":").Append(Q(string.Join(",", sec.Select(size => AddInventory(null, size, false)))));

                sb.Append(",\"pos\":\"").Append(Pos(p.X, p.Y, p.Z)).Append("\",\"rot\":").Append(Q(p.Rot)).Append(",\"planet\":").Append(planet);

                if (t.Color is not null)
                    sb.Append(",\"color\":").Append(Q(t.Color));
                if (t.Text is not null)
                    sb.Append(",\"text\":").Append(Q(t.Text));
                if (t.Pnls is not null)
                    sb.Append(",\"pnls\":").Append(Q(t.Pnls));

                sb.Append('}');
                newObjects.Add(sb.ToString());
            }

            if (objectCount == 0 && removedCount > 0)
                return new MainBaseOutcome(text, 0, 0, 0, Array.Empty<string>(), removedCount);

            if (objectCount == 0)
                return Fail(new[] { "Everything of the base is already there, so there is nothing to build." });

            var objectText = string.Concat(newObjects.Select(r => r + "|" + eol));
            var inventoryText = string.Concat(newInventories.Select(r => r + "|" + eol));
            var lastInventory = inventories.OrderBy(r => r.Start).Last();

            // The inventory insertion is later in the text, so it goes in first and the earlier one keeps its place.
            var newText = text.Insert(lastInventory.Start, inventoryText).Insert(beacon.Start, objectText);

            var problems = VerifyBuild(text, newText, objectText, inventoryText, objectIds, inventoryIds, links);
            return problems.Count > 0 ? Fail(problems) : new MainBaseOutcome(newText, objectCount, newInventories.Count, items, Array.Empty<string>(), removedCount);
        }

        // A JSON string the way the game's save writes it: only quotes, backslashes and control characters are escaped (an "&" stays an "&").
        private static string Q(string s)
        {
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (var c in s)
            {
                if (c is '"' or '\\')
                    sb.Append('\\').Append(c);
                else if (char.IsControl(c))
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else
                    sb.Append(c);
            }

            return sb.Append('"').ToString();
        }

        /// <summary>The game's own "supply everything" list as this save wrote it (it grows with game versions), else the dashboard's.</summary>
        private static IReadOnlyList<string> LearnEverythingFrom(IEnumerable<string> inventoryRecords)
        {
            foreach (var raw in inventoryRecords)
            {
                var match = Regex.Match(raw, "\"supplyGrps\":\"([^\"]*)\"");
                if (!match.Success)
                    continue;

                var list = match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (list.Length >= 100)
                    return list;
            }

            return DroneNetworkEngine.EverythingFallback;
        }
    }
}
