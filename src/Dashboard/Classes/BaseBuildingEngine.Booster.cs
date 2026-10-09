using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// One kind of booster: a stack of the best machine for a planet stat, all in one spot, with an optimizer beside it holding that stat's fuses.
    /// <paramref name="Key"/> is what a beacon's name starts with (<c>Heat</c>, <c>Pressure</c>).
    /// </summary>
    /// <param name="Ring">The machines surround the optimizer (which stands on the beacon) in a 3 by 3 block, instead of being stacked at the beacon with the optimizer beside them.
    /// Needed for machines the player has to open (a farm's larvae slots), which a stack would make unreachable.</param>
    /// <param name="MachineSlots">How many slots each machine's own inventory has when it needs one (a butterfly farm's three larvae slots); 0 when it has none (heaters, drills).</param>
    /// <param name="Spacing">The default distance between neighbouring machines of a ring, in metres.</param>
    /// <param name="MachineInventorySettings">Extra fields written on each machine's own inventory record, as the save writes them (a beehive's drone settings: it supplies honey and bee larvae).</param>
    public sealed record BoosterKind(string Key, string Name, string MachineGId, string MachineName, string FuseGId, string FuseName, int Machines,
        bool Ring = false, int MachineSlots = 0, double Spacing = 0, string MachineInventorySettings = "");

    /// <param name="MachinesFound">Machines of the kind already standing where they would go (a hand-placed one counts, and is not added again).</param>
    /// <param name="Spots">Where the machines still to be added go (a stack repeats one spot).</param>
    /// <param name="Spacing">The distance between a ring's neighbours (0 for a stack).</param>
    /// <param name="OptimizerExists">An optimizer already stands where this one goes, so none is added.</param>
    public sealed record BoosterPlan(
        BuildBeacon? Beacon, BoosterKind? Kind, double X, double Y, double Z, int MachinesFound, IReadOnlyList<(double X, double Z)> Spots,
        double OptimizerX, double OptimizerZ, bool OptimizerExists, IReadOnlyList<string> Problems, double Spacing = 0)
    {
        public int MachinesToAdd => Spots.Count;

        /// <summary>There is something to build, and nothing in the way.</summary>
        public bool Ok => Beacon is not null && Kind is not null && Problems.Count == 0 && (MachinesToAdd > 0 || !OptimizerExists);

        public bool AlreadyBuilt => Beacon is not null && Kind is not null && Problems.Count == 0 && MachinesToAdd == 0 && OptimizerExists;
    }

    /// <summary>What a booster build would write (or did). Nothing is returned when there is a problem.</summary>
    public sealed record BoosterOutcome(string? NewText, int Machines, int Optimizers, int Fuses, IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>
    /// The Base Building "Boosters": a beacon named for a planet stat (<c>Heat</c>, <c>Pressure</c>) gets, where it stands, a stack of that stat's best machine
    /// (8 of them, one on top of the other: the game lets them overlap and each makes its own share) and an optimizer 2 beside them, holding that stat's fuses.
    /// Found by the owner on 2026-10-09: he placed one T5 heater, had seven more written at the same position, and an optimizer filled with heat fuses beside it.
    ///
    /// The ids are the game's, not the tiers' numbering: the T5 heater is <c>Heater5</c> but the T5 drill is <c>Drill4</c>. The beacon is left as it is. The stack
    /// is at the beacon's own position and height, so nothing is checked for room (the machines overlap by design) and the ground is not checked either.
    /// </summary>
    public static partial class BaseBuildingEngine
    {
        public const int BoosterSlots = 3; // an Optimizer2 has three fuse slots (every one in the owner's saves has size 3)

        /// <summary>How far from the stack the optimizer stands, along the way the beacon points.</summary>
        public const double BoosterOptimizerDistance = 3; // a stack's optimizer; a ring's stands on the beacon itself

        public const string OptimizerGId = "Optimizer2";

        public static readonly IReadOnlyList<BoosterKind> Boosters = new[]
        {
            new BoosterKind("Heat", "Heat", "Heater5", "Heater T5", "FuseHeat1", "Heat Fuse", 8),
            new BoosterKind("Pressure", "Pressure", "Drill4", "Drill T5", "FusePressure1", "Pressure Fuse", 8),

            // The farms are the T2 (ButterflyFarm3 exists but is not unlocked yet). The ring spacing is a guess from how close the owner placed his own: 6.57 to 7 m.
            new BoosterKind("Butterflies", "Butterflies", "ButterflyFarm2", "Butterfly Farm T2", "FuseInsects1", "Insect Fuse", 8, Ring: true, MachineSlots: 3, Spacing: 6.6),

            // The owner's own beehives (T2, four slots) all supplied honey and bee larvae to the drones; new ones are written the same way. The owner's closest placement was 6.96 m, but he chose 3.3 m (half the butterflies' 6.6) as the default on 2026-10-09.
            new BoosterKind("Bees", "Bees", "Beehive2", "Beehive T2", "FuseInsects1", "Insect Fuse", 8, Ring: true, MachineSlots: 4, Spacing: 3.3,
                MachineInventorySettings: ",\"demandGrps\":\"\",\"supplyGrps\":\"honey,Bee1Larvae\",\"priority\":0")
        };

        /// <summary>The 3 by 3 block's eight places around its centre, in steps from it (the centre is the optimizer's).</summary>
        private static readonly (int Dx, int Dz)[] RingCells = { (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1) };

        /// <summary>The booster a beacon's name asks for ("Heat", "heat north", "Pressure 2"), or null when it names none.</summary>
        public static BoosterKind? BoosterFor(string beaconText) =>
            Boosters.FirstOrDefault(k => (beaconText ?? "").Trim().StartsWith(k.Key, StringComparison.OrdinalIgnoreCase));

        /// <param name="spacing">For a ring: the distance between neighbours in metres (the kind's own when null).</param>
        public static BoosterPlan PlanBooster(string text, long beaconId, double? spacing = null)
        {
            BoosterPlan Problem(string message, BuildBeacon? beacon = null, BoosterKind? kind = null) =>
                new(beacon, kind, 0, 0, 0, 0, Array.Empty<(double, double)>(), 0, 0, false, new[] { message });

            var world = Read(text);
            var beacon = Beacons(world).FirstOrDefault(b => b.Id == beaconId);
            if (beacon is null)
                return Problem("That beacon is not in the save.");

            var kind = BoosterFor(beacon.Text);
            if (kind is null)
                return Problem("The beacon's name does not start with " + string.Join(" or ", Boosters.Select(k => k.Key)) + ".", beacon);

            bool At(Obj o, string gId, double x, double z) =>
                o.GId == gId && Math.Abs(o.X - x) < 0.3 && Math.Abs(o.Z - z) < 0.3 && Math.Abs(o.Y - beacon.Y) < 0.5;

            var spots = new List<(double X, double Z)>();
            double ox, oz, step = 0;
            int found;

            if (kind.Ring)
            {
                // The optimizer stands on the beacon; the machines in the eight places around it, as tight as the spacing says. A machine already in a place stays.
                step = Math.Clamp(spacing ?? kind.Spacing, 2, 20);
                (ox, oz) = (beacon.X, beacon.Z);
                found = 0;
                foreach (var (dx, dz) in RingCells)
                {
                    double x = beacon.X + dx * step, z = beacon.Z + dz * step;
                    if (world.Objects.Any(o => At(o, kind.MachineGId, x, z)))
                        found++;
                    else
                        spots.Add((x, z));
                }
            }
            else
            {
                // The stack stands exactly where the beacon does; the optimizer a few metres on, along the way the beacon points.
                (ox, oz) = (beacon.X + beacon.DirX * BoosterOptimizerDistance, beacon.Z + beacon.DirZ * BoosterOptimizerDistance);
                found = world.Objects.Count(o => At(o, kind.MachineGId, beacon.X, beacon.Z));
                for (var i = found; i < kind.Machines; i++)
                    spots.Add((beacon.X, beacon.Z));
            }

            var optimizer = world.Objects.Any(o => At(o, OptimizerGId, ox, oz));
            return new BoosterPlan(beacon, kind, beacon.X, beacon.Y, beacon.Z, found, spots, ox, oz, optimizer, Array.Empty<string>(), step);
        }

        /// <summary>
        /// Writes a booster plan into the text of a save: the missing machines at the beacon, and (unless one already stands there) an optimizer 2 with its inventory
        /// of fuses. New records go in front of the beacon's own record, and the inventory in front of the last one, like the other builds. Nothing else changes:
        /// the proof is that taking out what was added gives back the original exactly. Any problem means no new text at all.
        /// </summary>
        public static BoosterOutcome ApplyBooster(string text, BoosterPlan plan, Random? random = null)
        {
            random ??= Rng;

            BoosterOutcome Fail(string message) => new(null, 0, 0, 0, new[] { message });

            if (plan.Beacon is null || plan.Kind is null || plan.Problems.Count > 0)
                return new BoosterOutcome(null, 0, 0, 0, plan.Problems.Count > 0 ? plan.Problems : new[] { "There is no beacon to build from." });

            if (plan.AlreadyBuilt)
                return Fail($"The {plan.Kind.Name.ToLowerInvariant()} booster is already built here, so nothing was built.");

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
                    return Fail($"A record at position {match.Index} is not valid JSON: {ex.Message}");
                }
            }

            var beacon = records.FirstOrDefault(r => !r.IsInventory && r.Id == plan.Beacon.Id && r.Raw.Contains("\"gId\":\"Beacon\"", StringComparison.Ordinal));
            var inventories = records.Where(r => r.IsInventory).ToList();
            if (beacon.Raw is null || inventories.Count == 0)
                return Fail("The beacon (or the save's inventories) could not be found in the text, so nothing was built.");

            var planet = Planet.Match(beacon.Raw).Groups[1].Value;
            if (planet.Length == 0)
                return Fail("The beacon has no planet, so nothing was built.");

            var used = new HashSet<long>(records.Select(r => r.Id));
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

            string Pos(double x, double y, double z) => string.Join(",", new[] { x, y, z }.Select(v => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture)));

            var newObjects = new List<string>();
            var newInventories = new List<string>();
            var objectIds = new List<long>();
            var inventoryIds = new List<long>();

            var nextInventory = inventories.Max(r => r.Id);

            foreach (var (sx, sz) in plan.Spots)
            {
                var id = NewObjectId();
                objectIds.Add(id);

                // A machine the player opens (a farm's larvae slots) has an inventory of its own, empty to begin with.
                var link = "";
                if (plan.Kind.MachineSlots > 0)
                {
                    var inventoryId = ++nextInventory;
                    inventoryIds.Add(inventoryId);
                    newInventories.Add($"{{\"id\":{inventoryId},\"woIds\":\"\",\"size\":{plan.Kind.MachineSlots}{plan.Kind.MachineInventorySettings}}}");
                    link = $",\"liId\":{inventoryId}";
                }

                newObjects.Add($"{{\"id\":{id},\"gId\":\"{plan.Kind.MachineGId}\"{link},\"pos\":\"{Pos(sx, plan.Y, sz)}\",\"rot\":\"0,0,0,1\",\"planet\":{planet}}}");
            }

            var optimizers = 0;
            var fuses = 0;
            if (!plan.OptimizerExists)
            {
                var held = new List<long>();
                for (var i = 0; i < BoosterSlots; i++)
                {
                    var fuseId = NewObjectId();
                    objectIds.Add(fuseId);
                    held.Add(fuseId);
                    newObjects.Add($"{{\"id\":{fuseId},\"gId\":\"{plan.Kind.FuseGId}\"}}");
                }

                var inventoryId = ++nextInventory;
                inventoryIds.Add(inventoryId);
                newInventories.Add($"{{\"id\":{inventoryId},\"woIds\":\"{string.Join(",", held)}\",\"size\":{BoosterSlots}}}");

                var optimizerId = NewObjectId();
                objectIds.Add(optimizerId);
                newObjects.Add($"{{\"id\":{optimizerId},\"gId\":\"{OptimizerGId}\",\"liId\":{inventoryId},\"pos\":\"{Pos(plan.OptimizerX, plan.Y, plan.OptimizerZ)}\",\"rot\":\"0,0,0,1\",\"planet\":{planet}}}");
                optimizers = 1;
                fuses = BoosterSlots;
            }

            var objectText = string.Concat(newObjects.Select(r => r + "|" + eol));
            var inventoryText = string.Concat(newInventories.Select(r => r + "|" + eol));
            var lastInventory = inventories.OrderBy(r => r.Start).Last();

            // The inventory insertion is later in the text, so it goes in first and the earlier one keeps its place.
            var newText = text.Insert(lastInventory.Start, inventoryText).Insert(beacon.Start, objectText);

            var problems = VerifyBuild(text, newText, objectText, inventoryText, objectIds, inventoryIds, inventoryIds);
            return problems.Count > 0 ? new BoosterOutcome(null, 0, 0, 0, problems) : new BoosterOutcome(newText, plan.MachinesToAdd, optimizers, fuses, Array.Empty<string>());
        }
    }
}
