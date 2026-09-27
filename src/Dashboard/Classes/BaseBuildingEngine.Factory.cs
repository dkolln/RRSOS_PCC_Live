using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// One of up to 4 crafters sharing a platform that nothing else claimed, each in its own corner (see <see cref="BaseBuildingEngine.PlanFactory"/>'s third pass): the owner's
    /// fallback for a product that still has nowhere to go after the ordinary one-crafter-per-platform pass and the local-chest pass, tried only on platforms with no crafter at
    /// all (mostly the blank aisle rows, which sit right next to every content row). <paramref name="X"/>/<paramref name="Z"/> are this corner's own position, 1.5 m off the
    /// platform's centre along each local axis.
    /// </summary>
    public sealed record FactoryQuarterTenant(string Product, double X, double Z, string? Label, IReadOnlyList<string>? LocalChestItems, double Farthest);

    /// <summary>One platform of the factory floor: where it is, whether a foundation is already there, the product whose crafter stands on it (if any), and what is in the way.</summary>
    public sealed record FactoryFloorCell(int Index, double X, double Z, bool Laid, bool FoundationExists, string? Product, double Farthest, IReadOnlyList<string> Conflicts, string? Label = null,
        IReadOnlyList<string>? LocalChestItems = null, IReadOnlyList<FactoryQuarterTenant>? Quarters = null);

    /// <summary>
    /// A plan for the factory: crafters on a floor of platforms above the warehouse, one crafter per platform, each set to make one product from the warehouse
    /// chests around and below it. <c>NotBuilt</c> lists the wanted products left out and why (no recipe, another machine, or ingredients out of reach).
    /// </summary>
    public sealed record FactoryBuildPlan(BuildBeacon Beacon, double Range, double Height, double FloorY, IReadOnlyList<FactoryFloorCell> Floor, IReadOnlyList<FactoryProduct> NotBuilt,
        IReadOnlyList<string> Problems, double PowerKw)
    {
        public int Crafters => Floor.Count(c => c.Product is not null) + Floor.Sum(c => c.Quarters?.Count ?? 0);
        public bool Ok => Problems.Count == 0 && Crafters > 0 && Floor.All(c => c.Conflicts.Count == 0);
    }

    /// <summary>A crafter of the factory as the save has it: which product it makes (what its inventory supplies), and whether it is switched on (set to that recipe).</summary>
    public sealed record FactoryCrafter(long Id, string Product, string? Recipe, double X, double Z, bool On);

    public sealed record FactoryToggleOutcome(string? NewText, int Changed, IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>The result of adding or removing one crafter, ad hoc, on a floor that already stands (see <see cref="BaseBuildingEngine.AddCrafter"/> / <c>RemoveCrafter</c>).</summary>
    public sealed record CrafterEditOutcome(string? NewText, IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>
    /// One tile of a ramp, captured from the owner's own build in Custom-2 (2026-09-27), given for a beacon pointing south (turned the same way everything else here turns for
    /// another direction). <paramref name="GId"/> null means the factory floor's usual flat tile is taken away here (an open drop the ramp's incline passes through, or the flat
    /// landing at warehouse level at the bottom); otherwise a tile of that id replaces it, at <paramref name="Dy"/> metres above the WAREHOUSE foundation (not the factory floor)
    /// and turned to <paramref name="Rot"/>.
    /// </summary>
    public sealed record RampPiece(int Index, string? GId, double Dy, string? Rot);

    public sealed record RampOutcome(string? NewText, int Removed, int Added, IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>A ramp: which platforms it touches (see <see cref="RampKind"/>) and its tiles. Belongs to the factory build, not the warehouse (the warehouse stays flat).</summary>
    public sealed record RampSpec(RampKind Kind, IReadOnlyList<RampPiece> Pieces);

    /// <summary><see cref="RampSpec.Kind"/>: which platforms a ramp's pieces index into.</summary>
    public enum RampKind
    {
        /// <summary>Along the row nearest the beacon (row 1), indexed by column (1 = nearest the beacon).</summary>
        EastByColumn,
        /// <summary>Down the last column of every row (the far end added by the 11-deep change), indexed by row (1 = nearest the beacon).</summary>
        BackByRow
    }

    public static partial class BaseBuildingEngine
    {
        /// <summary>The crafter the game places (AutoCrafter1), and where its inventory record says its output goes: 8 slots.</summary>
        public const string CrafterGId = "AutoCrafter1";
        private const int CrafterSlots = 8;
        private const int LocalChestSlots = 15; // Container1
        private const int HighPriority = 5; // the top of the game's demand-priority scale (-3..5)
        private const double LocalChestOffset = 1.8;
        private const double QuarterOffset = 1.5; // a quartered platform's corner, off the centre along each local axis

        /// <summary>
        /// Reflects <paramref name="pointX"/>/<paramref name="pointZ"/> to the far side of <paramref name="centerX"/>/<paramref name="centerZ"/> along the beacon's own
        /// forward axis only — its position across that axis (left/right) is kept the same. This is how a local chest's spot is found from its crafter's: the owner's
        /// "put the AC in a southern corner, the container in the opposite northern corner" — the chest sits in the corner directly on the other side, never sideways.
        /// </summary>
        private static (double X, double Z) MirrorAlongBeacon(double centerX, double centerZ, double pointX, double pointZ, int dirX, int dirZ)
        {
            var along = (pointX - centerX) * dirX + (pointZ - centerZ) * dirZ;
            return (pointX - 2 * along * dirX, pointZ - 2 * along * dirZ);
        }

        /// <summary>
        /// Where a tenant's local chest should stand: its own platform's opposite corner (see <see cref="MirrorAlongBeacon"/>), unless another tenant already sits
        /// exactly there — which happens when two tenants of the same platform are themselves a mirror pair (same side, opposite ends) — in which case the chest
        /// tries the platform's other two corners instead, and only as a last resort (every corner already taken) sits further out along the tenant's own diagonal.
        /// </summary>
        private static (double X, double Z) ChestSpotFor(FactoryQuarterTenant tenant, IReadOnlyList<FactoryQuarterTenant> siblings, double platformX, double platformZ, int dirX, int dirZ)
        {
            bool Occupied(double x, double z) => siblings.Any(s => Math.Abs(s.X - x) < 0.2 && Math.Abs(s.Z - z) < 0.2);

            var mirrored = MirrorAlongBeacon(platformX, platformZ, tenant.X, tenant.Z, dirX, dirZ);
            if (!Occupied(mirrored.X, mirrored.Z))
                return mirrored;

            var (rx, rz) = (dirZ, -dirX);
            var along = (tenant.X - platformX) * dirX + (tenant.Z - platformZ) * dirZ;
            var across = (tenant.X - platformX) * rx + (tenant.Z - platformZ) * rz;

            var sameAlong = (platformX + along * dirX - across * rx, platformZ + along * dirZ - across * rz);
            if (!Occupied(sameAlong.Item1, sameAlong.Item2))
                return sameAlong;

            var farDiagonal = (platformX - along * dirX - across * rx, platformZ - along * dirZ - across * rz);
            if (!Occupied(farDiagonal.Item1, farDiagonal.Item2))
                return farDiagonal;

            // Every corner already has a tenant (all 4 sharing this platform): the only room left is further out along the tenant's own diagonal.
            return (platformX + (tenant.X - platformX) * 1.53, platformZ + (tenant.Z - platformZ) * 1.53);
        }
        private const double CrafterSignOffset = 0.99, CrafterSignUp = 2.58;
        private const string CrafterRot = "0,1,0,-4.371139E-08";

        /// <summary>
        /// Plans the factory: for every wanted product that an autocrafter can make and whose ingredient chests are all in range from some warehouse platform, a crafter
        /// on a platform of a floor <paramref name="height"/> metres above the warehouse, one crafter per platform, the platforms assigned so that products with the
        /// fewest choices go first and each takes its best free platform. Only buildings are checked for room; the ground is not known.
        /// </summary>
        /// <param name="warehouse">The warehouse's platforms (its plan), whose centres are the places a crafter can stand.</param>
        public static FactoryBuildPlan PlanFactory(string text, long beaconId, BuildTemplate template, IReadOnlyList<PlannedPlatform> warehouse, RecipeBook book, IEnumerable<string> products,
            double height, bool fullFloor, double crafterKw, Func<string, string> labelOf, Func<string, bool> isBuilding)
        {
            var world = Read(text);
            var beacon = Beacons(world).FirstOrDefault(b => b.Id == beaconId);
            var problems = new List<string>();

            if (beacon is null)
                return new FactoryBuildPlan(new BuildBeacon(beaconId, "", 0, 0, 0, 0, 0, 0, null, 0, 0, 0), 0, height, 0, Array.Empty<FactoryFloorCell>(), Array.Empty<FactoryProduct>(),
                    new[] { "That beacon is not in the save." }, 0);

            var floorY = beacon.FoundationY + height;
            var range = book.AutoCrafter?.Range ?? 0;

            if (warehouse.Count == 0)
                problems.Add("There is no warehouse plan to build above.");

            if (range <= 0)
                problems.Add("recipes.json has no autocrafter range. Start the game once with plugin 0.9.0 or later.");

            var spots = warehouse.Select(p => new FactorySpot(p.Index, p.X, p.Z)).ToList();
            var chests = FactoryPlanner.WarehouseChests(text, spots.Select(s => (s.X, s.Z)), template.Spacing, beacon.FoundationY);
            if (problems.Count == 0 && chests.Count == 0)
                problems.Add("No warehouse chests were found under the platforms, so nothing can be reached. Build the warehouse first.");

            if (problems.Count > 0)
                return new FactoryBuildPlan(beacon, range, height, floorY, Array.Empty<FactoryFloorCell>(), Array.Empty<FactoryProduct>(), problems, 0);

            var reaches = FactoryPlanner.Evaluate(book, chests, spots, beacon.FoundationY, height, products.Distinct(StringComparer.Ordinal).ToList());

            // Fewest choices first, each taking its nearest-farthest free platform.
            var taken = new Dictionary<int, (string Product, double Far)>();
            var notBuilt = new List<FactoryProduct>();
            var crafterY = floorY + OnFoundation;

            foreach (var r in reaches.Where(r => r.Mode == FactoryMode.InRange).OrderBy(r => r.Full.Count).ThenBy(r => r.Product, StringComparer.Ordinal))
            {
                var pick = r.Full.FirstOrDefault(f => !taken.ContainsKey(f.Spot.Index));
                if (pick.Spot is null)
                {
                    notBuilt.Add(new FactoryProduct(r.Product, FactoryMode.InRange, r.Ingredients, null, r.Reached, r.Missing, r.BestFarthest, "Every platform that reaches its ingredients already has a crafter."));
                    continue;
                }

                taken[pick.Spot.Index] = (r.Product, pick.Farthest);
            }

            // Corners of a platform, 1.5 m off its centre along the beacon's own axes: index 0 is the "south" one (further from the beacon), index 2 the
            // "north" one directly opposite it (same left/right side, the other way along); 1 and 3 are the same pair on the other side.
            var (rx, rz) = (beacon.DirZ, -beacon.DirX);
            var corners = new (double Along, double Across)[] { (QuarterOffset, QuarterOffset), (QuarterOffset, -QuarterOffset), (-QuarterOffset, QuarterOffset), (-QuarterOffset, -QuarterOffset) };
            FactorySpot[] CornersOf(FactorySpot s, int baseIndex) => corners.Select((c, i) => new FactorySpot(baseIndex + i, s.X + c.Along * beacon.DirX + c.Across * rx, s.Z + c.Along * beacon.DirZ + c.Across * rz)).ToArray();

            // The distance a product reaches its farthest ingredient from a specific corner spot: the real value if that corner reaches everything, else the
            // fallback the caller already worked out for wherever it landed.
            double NewSpotFarthest(ProductReach r, FactorySpot spot)
            {
                var match = r.Full.FirstOrDefault(f => f.Spot.Index == spot.Index);
                return match.Spot is not null ? match.Farthest : r.BestFarthest;
            }

            var quarterAssignments = new Dictionary<int, List<FactoryQuarterTenant>>();

            // Second pass: a product whose ingredients no single platform reaches, or one that lost its tie for the only platform that does, gets whatever
            // platform is left over that reaches the most of it — in its south corner (away from the beacon), re-checked there, with a local Container1 in the
            // opposite, north, corner demanding the rest (see ApplyFactory) — the owner's fallback, "if all else fails". Re-evaluated against only the platforms
            // not already taken, so it never displaces a product placed in the first pass.
            var pending = reaches.Where(r => r.Mode is FactoryMode.InRange or FactoryMode.LocalChest && !taken.Values.Any(t => t.Product == r.Product)).Select(r => r.Product).ToList();
            var freeSpots = spots.Where(s => !taken.ContainsKey(s.Index)).ToList();

            if (pending.Count > 0 && freeSpots.Count > 0)
            {
                var retry = FactoryPlanner.Evaluate(book, chests, freeSpots, beacon.FoundationY, height, pending);
                foreach (var r in retry.Where(r => r.Reached.Count > 0).OrderByDescending(r => r.Reached.Count).ThenBy(r => r.Product, StringComparer.Ordinal))
                {
                    var full = r.Full.FirstOrDefault(f => !taken.ContainsKey(f.Spot.Index) && !quarterAssignments.ContainsKey(f.Spot.Index));
                    if (full.Spot is not null)
                    {
                        taken[full.Spot.Index] = (r.Product, full.Farthest);
                        continue;
                    }

                    var best = r.Best;
                    if (best is null || taken.ContainsKey(best.Index) || quarterAssignments.ContainsKey(best.Index))
                        continue;

                    var south = CornersOf(best, 0)[0];
                    var southReach = FactoryPlanner.Evaluate(book, chests, new[] { south }, beacon.FoundationY, height, new[] { r.Product })[0];
                    if (southReach.Reached.Count == 0)
                        continue; // the corner itself reaches nothing (rare); leave it for the third pass to try a different platform

                    quarterAssignments[best.Index] = new List<FactoryQuarterTenant>
                        { new(r.Product, south.X, south.Z, labelOf(r.Product), southReach.Missing.Count > 0 ? southReach.Missing : null, southReach.BestFarthest) };
                }
            }

            // A product placed by the second pass may already sit in notBuilt from the first (it lost the tie for the one platform that reached everything);
            // the second pass found it a home after all, so that entry is stale.
            notBuilt.RemoveAll(n => taken.Values.Any(t => t.Product == n.Product) || quarterAssignments.Values.Any(list => list.Any(q => q.Product == n.Product)));

            // Third pass: a product still with nowhere to go (every platform that reaches any of it is already someone's) gets a corner of its own, 1.5 m off the
            // centre, on a platform nothing else claimed at all — mostly the blank aisle rows, which sit right next to every content row so a corner there
            // reaches about as well as the platform itself would. Up to 4 products can share one platform this way, each with its own local chest if it needs one.
            // Tried only for stragglers: a product that already has a platform (even a shared corner) is never moved here.
            var stillPending = pending.Where(p => !taken.Values.Any(t => t.Product == p) && !quarterAssignments.Values.Any(list => list.Any(q => q.Product == p))).ToList();
            var unusedSpots = spots.Where(s => !taken.ContainsKey(s.Index) && !quarterAssignments.ContainsKey(s.Index)).ToList();

            if (stillPending.Count > 0 && unusedSpots.Count > 0)
            {
                var pool = unusedSpots.SelectMany(s => CornersOf(s, s.Index * 4)).ToList();
                var quarterReach = FactoryPlanner.Evaluate(book, chests, pool, beacon.FoundationY, height, stillPending);
                var takenCorners = new HashSet<int>();

                foreach (var r in quarterReach.Where(r => r.Reached.Count > 0).OrderByDescending(r => r.Reached.Count).ThenBy(r => r.Product, StringComparer.Ordinal))
                {
                    var full = r.Full.FirstOrDefault(f => !takenCorners.Contains(f.Spot.Index));
                    var spot = full.Spot ?? (r.Best is { } b && !takenCorners.Contains(b.Index) ? b : null);
                    if (spot is null)
                        continue;

                    takenCorners.Add(spot.Index);
                    var platformIndex = spot.Index / 4;
                    if (!quarterAssignments.TryGetValue(platformIndex, out var list))
                        quarterAssignments[platformIndex] = list = new List<FactoryQuarterTenant>();

                    list.Add(new FactoryQuarterTenant(r.Product, spot.X, spot.Z, labelOf(r.Product), r.Missing.Count > 0 ? r.Missing : null, full.Spot is not null ? full.Farthest : r.BestFarthest));
                }
            }

            notBuilt.RemoveAll(n => quarterAssignments.Values.Any(list => list.Any(q => q.Product == n.Product)));

            // Fourth pass: the owner's own idea — a stubborn product whose one good spot is a platform someone else already has to themselves gets it anyway: that
            // platform is converted to quarters too, its original tenant moved off-centre into one corner (re-checked there so it never loses reach doing it),
            // the stubborn product into another. Tried last, and only for what nothing else could place.
            var stillStuck = pending.Where(p => !taken.Values.Any(t => t.Product == p) && !quarterAssignments.Values.Any(list => list.Any(q => q.Product == p))).ToList();

            if (stillStuck.Count > 0 && taken.Count > 0)
            {
                var takenPlatforms = spots.Where(s => taken.ContainsKey(s.Index)).ToList();
                var pool = takenPlatforms.SelectMany(s => CornersOf(s, s.Index * 4)).ToList();
                var retrofitReach = FactoryPlanner.Evaluate(book, chests, pool, beacon.FoundationY, height, stillStuck);
                var convertedPlatforms = new HashSet<int>();
                var usedCorners = new HashSet<int>();

                foreach (var r in retrofitReach.Where(r => r.Reached.Count > 0).OrderByDescending(r => r.Reached.Count).ThenBy(r => r.Product, StringComparer.Ordinal))
                {
                    var candidates = (r.Full.Select(f => f.Spot).Any() ? r.Full.Select(f => f.Spot) : r.Best is { } b0 ? new[] { b0 } : Array.Empty<FactorySpot>())
                        .Where(s => !usedCorners.Contains(s.Index) && !convertedPlatforms.Contains(s.Index / 4));
                    var newSpot = candidates.FirstOrDefault();
                    if (newSpot is null)
                        continue;

                    var platformIndex = newSpot.Index / 4;
                    var platform = takenPlatforms.First(s => s.Index == platformIndex);
                    var (originalProduct, _) = taken[platformIndex];

                    // Move the original tenant to a different corner of the same platform, and confirm it still reaches at least as much as it did centred.
                    var originalIngredients = reaches.First(x => x.Product == originalProduct).Ingredients.Keys.ToList();
                    var originalReachedBefore = reaches.First(x => x.Product == originalProduct).Reached.Count;
                    var ownCorners = CornersOf(platform, platformIndex * 4).Where(c => c.Index != newSpot.Index).ToList();
                    var movedReach = FactoryPlanner.Evaluate(book, chests, ownCorners, beacon.FoundationY, height, new[] { originalProduct })[0];
                    if (movedReach.Reached.Count < originalReachedBefore)
                        continue; // moving it would make it worse off; leave this platform alone

                    var movedSpot = movedReach.Full.FirstOrDefault().Spot ?? movedReach.Best;
                    if (movedSpot is null)
                        continue;

                    taken.Remove(platformIndex);
                    convertedPlatforms.Add(platformIndex);
                    usedCorners.Add(newSpot.Index);
                    usedCorners.Add(movedSpot.Index);
                    quarterAssignments[platformIndex] = new List<FactoryQuarterTenant>
                    {
                        new(originalProduct, movedSpot.X, movedSpot.Z, labelOf(originalProduct), movedReach.Missing.Count > 0 ? movedReach.Missing : null,
                            movedReach.Full.Count > 0 ? movedReach.Full[0].Farthest : movedReach.BestFarthest),
                        new(r.Product, newSpot.X, newSpot.Z, labelOf(r.Product), r.Missing.Count > 0 ? r.Missing : null, NewSpotFarthest(r, newSpot))
                    };
                }
            }

            notBuilt.RemoveAll(n => quarterAssignments.Values.Any(list => list.Any(q => q.Product == n.Product)));

            foreach (var r in reaches.Where(r => r.Mode != FactoryMode.InRange && !taken.Values.Any(t => t.Product == r.Product) && !quarterAssignments.Values.Any(list => list.Any(q => q.Product == r.Product))))
                notBuilt.Add(new FactoryProduct(r.Product, r.Mode, r.Ingredients, r.Best is { } b ? (b.X, crafterY, b.Z) : null, r.Reached, r.Missing, r.BestFarthest, r.Note));

            var half = template.Spacing / 2;
            var floor = new List<FactoryFloorCell>();

            foreach (var p in warehouse)
            {
                taken.TryGetValue(p.Index, out var t);
                var quarters = quarterAssignments.TryGetValue(p.Index, out var qList) ? qList : null;
                var laid = fullFloor || t.Product is not null || quarters is not null;
                var exists = world.Objects.Any(o => o.GId == "Foundation" && Math.Abs(o.X - p.X) < 0.6 && Math.Abs(o.Z - p.Z) < 0.6 && Math.Abs(o.Y - floorY) < 0.6);

                var conflicts = !laid
                    ? new List<string>()
                    : world.Objects
                        .Where(o => o.Id != beacon.Id && isBuilding(o.GId)
                                    && Math.Abs(o.X - p.X) < half - 0.05 && Math.Abs(o.Z - p.Z) < half - 0.05
                                    && o.Y > floorY - 1 && o.Y < floorY + 8
                                    && !(o.GId == "Foundation" && Math.Abs(o.X - p.X) < 0.6 && Math.Abs(o.Z - p.Z) < 0.6 && Math.Abs(o.Y - floorY) < 0.6))
                        .Take(4)
                        .Select(o => $"{o.GId} at ({o.X:0.#}, {o.Z:0.#})")
                        .ToList();

                floor.Add(new FactoryFloorCell(p.Index, p.X, p.Z, laid, exists, t.Product, t.Far, conflicts, t.Product is null ? null : labelOf(t.Product), null, quarters));
            }

            return new FactoryBuildPlan(beacon, range, height, floorY, floor, notBuilt.OrderBy(n => n.Product, StringComparer.Ordinal).ToList(), problems, floor.Count(c => c.Product is not null) * crafterKw);
        }

        /// <summary>The factory's footprint as a plan for the removal code: the warehouse platforms, lifted to the factory floor, with nothing on them.</summary>
        public static BuildPlan FactoryFootprint(BuildBeacon beacon, BuildTemplate template, IReadOnlyList<PlannedPlatform> warehouse, double height) =>
            new(beacon, template, Array.Empty<string>(),
                warehouse.Select(p => new PlannedPlatform(p.Index, p.X, beacon.FoundationY + height, p.Z, false, Array.Empty<PlannedChest>(), Array.Empty<string>())).ToList(),
                Array.Empty<string>());

        /// <summary>
        /// Writes a factory plan into the text of a save: a foundation on each laid platform that has none, and on each crafter's platform an AutoCrafter1 (labelled with its
        /// product, set to that recipe when <paramref name="on"/>), with its own inventory of 8 slots that supplies the product to the drones (so the warehouse chest that
        /// demands it is filled). The proof is the same as for the warehouse: taking out exactly what was added gives back the original save.
        /// </summary>
        public static BuildOutcome ApplyFactory(string text, FactoryBuildPlan plan, bool on, Random? random = null)
        {
            random ??= Rng;

            if (plan.Problems.Count > 0)
                return Failed(plan.Problems.ToList());

            var conflict = plan.Floor.FirstOrDefault(c => c.Conflicts.Count > 0);
            if (conflict is not null)
                return Failed($"Platform {conflict.Index} is not clear ({string.Join(", ", conflict.Conflicts)}), so nothing was built.");

            if (plan.Crafters == 0)
                return Failed("There is no crafter to build.");

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
            var links = new List<long>();
            var foundations = 0;
            var crafters = 0;
            var signs = 0;
            var localChests = 0;

            // Signs are turned like the warehouse's: identity when the beacon points south, the same turn from south for another direction.
            var signTurn = (int)Math.Round(-Math.Atan2(-plan.Beacon.DirZ, -plan.Beacon.DirX) * 180 / Math.PI);
            var signRot = TurnRot("0,0,0,1", signTurn);
            var crafterRot = TurnRot(CrafterRot, signTurn); // the owner's crafters (beacon pointing south) face the beacon; for another direction they turn with it
            var chestRot = TurnRot(crafterRot, 180); // the owner: a local chest should face the opposite way from the crafter (it was facing the same way, wrongly)

            foreach (var cell in plan.Floor.Where(c => c.Laid))
            {
                if (!cell.FoundationExists)
                {
                    var foundationId = NewObjectId();
                    objectIds.Add(foundationId);
                    newObjects.Add($"{{\"id\":{foundationId},\"gId\":\"Foundation\",\"pos\":\"{Pos(cell.X, plan.FloorY, cell.Z)}\",\"rot\":\"0,0,0,1\",\"planet\":{planet}}}");
                    foundations++;
                }

                // One crafter (+ sign, + a local chest if it needs one), at (cx, cz): the platform's own centre for a normal cell, or one corner of a quartered one.
                // (chestX, chestZ) is where its local chest would sit if it needs one.
                void WriteCrafter(double cx, double cz, string product, string? label, IReadOnlyList<string>? localItems, double chestX, double chestZ)
                {
                    do { nextInventory++; } while (!used.Add(nextInventory));
                    newInventories.Add($"{{\"id\":{nextInventory},\"woIds\":\"\",\"size\":{CrafterSlots},\"demandGrps\":\"\",\"supplyGrps\":\"{product}\",\"priority\":0}}");
                    inventoryIds.Add(nextInventory);
                    links.Add(nextInventory);

                    var crafterId = NewObjectId();
                    objectIds.Add(crafterId);
                    var recipe = on ? $",\"liGrps\":\"{product}\"" : "";
                    newObjects.Add($"{{\"id\":{crafterId},\"gId\":\"{CrafterGId}\",\"liId\":{nextInventory}{recipe},\"pos\":\"{Pos(cx, plan.FloorY + OnFoundation, cz)}\",\"rot\":\"{crafterRot}\",\"planet\":{planet}}}");
                    crafters++;

                    // A crafter has no text label of its own, so a sign hangs on the side facing the beacon (as the owner hung his, 0.99 m in front and 2.58 m up).
                    var signId = NewObjectId();
                    objectIds.Add(signId);
                    var (sx, sz) = (cx - CrafterSignOffset * plan.Beacon.DirX, cz - CrafterSignOffset * plan.Beacon.DirZ);
                    newObjects.Add($"{{\"id\":{signId},\"gId\":\"Sign\",\"pos\":\"{Pos(sx, plan.FloorY + OnFoundation + CrafterSignUp, sz)}\",\"rot\":\"{signRot}\",\"planet\":{planet},\"text\":\"{label ?? product}\"}}");
                    signs++;

                    // The owner's fallback for a recipe no single platform reaches in full: a Container1 beside the crafter, demanding whatever the crafter's
                    // spot cannot reach, at high priority (5, the top of the game's -3..5 scale) so drones fill it first — the owner's own sample
                    // ("OsmiumCrafting") does the same thing by hand, just with one item and priority 1.
                    if (localItems is { Count: > 0 })
                    {
                        do { nextInventory++; } while (!used.Add(nextInventory));
                        newInventories.Add($"{{\"id\":{nextInventory},\"woIds\":\"\",\"size\":{LocalChestSlots},\"demandGrps\":\"{string.Join(",", localItems)}\",\"supplyGrps\":\"\",\"priority\":{HighPriority}}}");
                        inventoryIds.Add(nextInventory);
                        links.Add(nextInventory);

                        var chestId = NewObjectId();
                        objectIds.Add(chestId);
                        newObjects.Add($"{{\"id\":{chestId},\"gId\":\"Container1\",\"liId\":{nextInventory},\"pos\":\"{Pos(chestX, plan.FloorY + OnFoundation, chestZ)}\",\"rot\":\"{chestRot}\",\"planet\":{planet},\"text\":\"{(label ?? product) + " inputs"}\"}}");
                        localChests++;
                    }
                }

                if (cell.Product is not null)
                {
                    var (rx, rz) = (plan.Beacon.DirZ, -plan.Beacon.DirX); // the beacon's right hand, same as the warehouse rows
                    WriteCrafter(cell.X, cell.Z, cell.Product, cell.Label, cell.LocalChestItems, cell.X + LocalChestOffset * rx, cell.Z + LocalChestOffset * rz);
                }

                // A quartered platform: up to 4 stragglers share it, each in its own corner. A tenant's local chest sits a little further out along the same
                // diagonal as its own corner, so it never reaches into a neighbour's.
                var quarters = cell.Quarters ?? Array.Empty<FactoryQuarterTenant>();
                foreach (var tenant in quarters)
                {
                    var (chestX, chestZ) = ChestSpotFor(tenant, quarters.Where(t => t != tenant).ToList(), cell.X, cell.Z, plan.Beacon.DirX, plan.Beacon.DirZ);
                    WriteCrafter(tenant.X, tenant.Z, tenant.Product, tenant.Label, tenant.LocalChestItems, chestX, chestZ);
                }
            }

            var objectText = string.Concat(newObjects.Select(r => r + "|" + eol));
            var inventoryText = string.Concat(newInventories.Select(r => r + "|" + eol));
            var lastInventory = inventories.OrderBy(r => r.Start).Last();

            var newText = text.Insert(lastInventory.Start, inventoryText).Insert(beacon.Start, objectText);

            var problems = VerifyBuild(text, newText, objectText, inventoryText, objectIds, inventoryIds, links);
            return problems.Count > 0 ? Failed(problems) : new BuildOutcome(newText, foundations, crafters + localChests, crafters, 0, Array.Empty<string>(), signs, localChests);
        }

        // The two ramps the owner built in Custom-2 (2026-09-27) and asked to have reproduced, captured tile by tile from that save. Both belong to the factory build (they replace
        // or remove tiles of the factory floor only; the warehouse underneath stays flat, 8 rows x 11). Dy is metres above the WAREHOUSE foundation.
        public static readonly RampSpec EastRamp = new(RampKind.EastByColumn, new[]
        {
            new RampPiece(1, null, 0, null),                                     // open landing at warehouse level, at the bottom
            new RampPiece(2, "FoundationSlope", 5.15, "0,-1,0,0"),
            new RampPiece(3, null, 0, null),                                     // the incline passes through here; no tile
            new RampPiece(4, "FoundationSlope", 10, "0,-1,0,0")                  // the top, level with the factory floor
        });

        public static readonly RampSpec BackRamp = new(RampKind.BackByRow, new[]
        {
            new RampPiece(4, "FoundationSlope", 10, "0,0.7071068,0,0.7071068"),  // the top, level with the factory floor
            new RampPiece(5, null, 0, null),                                     // the incline passes through here; no tile
            new RampPiece(6, "FoundationSlope", 5.15, "0,0.7071068,0,0.7071068"),
            new RampPiece(7, null, 0, null),                                     // open landing at warehouse level
            new RampPiece(8, null, 0, null)                                      // open landing at warehouse level
        });

        /// <summary>
        /// Builds or fixes up one ramp on the factory floor: for each of its pieces, removes the factory floor's usual flat tile if one is there and, unless the piece calls for
        /// an open drop, puts the wanted tile (a <c>FoundationSlope</c>, turned the same way the crafters and signs are for this beacon's direction) in its place. Already-correct
        /// pieces are left untouched, so running it again after a partial edit finishes the job. The warehouse (the platforms at ground level) is never touched.
        /// </summary>
        public static RampOutcome ApplyRamp(string text, BuildPlan warehouse, double height, RampSpec ramp, Random? random = null)
        {
            random ??= Rng;

            if (warehouse.Problems.Count > 0)
                return new RampOutcome(null, 0, 0, warehouse.Problems.ToList());

            var width = warehouse.Platforms.Count(p => p.Row == 1);
            if (width == 0)
                return new RampOutcome(null, 0, 0, new[] { "The warehouse has no platforms to build a ramp over." });

            var beacon = warehouse.Beacon;
            var floorY = beacon.FoundationY + height;
            var targets = new List<(PlannedPlatform Platform, RampPiece Piece)>();

            foreach (var piece in ramp.Pieces)
            {
                var platform = ramp.Kind == RampKind.EastByColumn
                    ? warehouse.Platforms.FirstOrDefault(p => p.Row == 1 && p.Index - (p.Row - 1) * width == piece.Index)
                    : warehouse.Platforms.FirstOrDefault(p => p.Index - (p.Row - 1) * width == width && p.Row == piece.Index);

                if (platform is null)
                    return new RampOutcome(null, 0, 0, new[] { $"Platform {piece.Index} of this ramp is not part of the warehouse (build it, or with enough rows/columns, first)." });

                targets.Add((platform, piece));
            }

            var world = Read(text);
            var half = warehouse.Template.Spacing / 2 - 0.05;
            var signTurn = (int)Math.Round(-Math.Atan2(-beacon.DirZ, -beacon.DirX) * 180 / Math.PI);

            var toRemove = new List<long>();
            var toAdd = new List<(double X, double Y, double Z, string GId, string Rot)>();

            foreach (var (platform, piece) in targets)
            {
                var flat = world.Objects.FirstOrDefault(o => o.GId == "Foundation" && Math.Abs(o.X - platform.X) < 0.6 && Math.Abs(o.Z - platform.Z) < 0.6 && Math.Abs(o.Y - floorY) < 0.6);
                var slope = world.Objects.FirstOrDefault(o => o.GId == "FoundationSlope" && Math.Abs(o.X - platform.X) < half && Math.Abs(o.Z - platform.Z) < half
                                                              && o.Y > beacon.FoundationY - 1 && o.Y < floorY + 1);

                if (piece.GId is null)
                {
                    if (flat is not null) toRemove.Add(flat.Id);
                    if (slope is not null) toRemove.Add(slope.Id);
                    continue;
                }

                var wantY = beacon.FoundationY + piece.Dy;
                var wantRot = TurnRot(piece.Rot!, signTurn);
                if (slope is not null && Math.Abs(slope.Y - wantY) < 0.1 && string.Equals(slope.Rot, wantRot, StringComparison.Ordinal))
                    continue; // already exactly this piece

                if (flat is not null) toRemove.Add(flat.Id);
                if (slope is not null) toRemove.Add(slope.Id);
                toAdd.Add((platform.X, wantY, platform.Z, piece.GId, wantRot));
            }

            if (toRemove.Count == 0 && toAdd.Count == 0)
                return new RampOutcome(null, 0, 0, new[] { "This ramp is already built exactly as planned; there is nothing to change." });

            var working = text;
            if (toRemove.Count > 0)
            {
                var (removed, removeProblems) = RemoveByIds(working, toRemove, Array.Empty<long>());
                if (removeProblems.Count > 0)
                    return new RampOutcome(null, 0, 0, removeProblems);

                working = removed!;
            }

            var added = 0;
            if (toAdd.Count > 0)
            {
                var records = ReadRecords(working);
                var beaconRec = records.FirstOrDefault(r => !r.IsInventory && r.Id == beacon.Id);
                if (beaconRec.Id != beacon.Id)
                    return new RampOutcome(null, toRemove.Count, 0, new[] { "The beacon could not be found after removing the old tiles, so nothing more was written." });

                var planet = Planet.Match(working.Substring(beaconRec.Start, beaconRec.Length)).Groups[1].Value;
                var used = new HashSet<long>(records.Select(r => r.Id));

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

                var eol = working.Contains("|\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var objectIds = new List<long>();
                var newObjects = new List<string>();
                foreach (var (x, y, z, gId, rot) in toAdd)
                {
                    var id = NewObjectId();
                    objectIds.Add(id);
                    newObjects.Add($"{{\"id\":{id},\"gId\":\"{gId}\",\"pos\":\"{Pos(x, y, z)}\",\"rot\":\"{rot}\",\"planet\":{planet}}}");
                }

                var objectText = string.Concat(newObjects.Select(r => r + "|" + eol));
                var afterInsert = working.Insert(beaconRec.Start, objectText);
                var problems = VerifyBuild(working, afterInsert, objectText, "", objectIds, new List<long>(), new List<long>());
                if (problems.Count > 0)
                    return new RampOutcome(null, toRemove.Count, 0, problems);

                working = afterInsert;
                added = newObjects.Count;
            }

            return new RampOutcome(working, toRemove.Count, added, Array.Empty<string>());
        }

        /// <summary>
        /// Adds one crafter, ad hoc, to a platform of a factory floor that already stands (the owner: build a factory, then add or remove crafters one at a time as the
        /// products change). Writes a foundation only if the platform has none yet; then an <c>AutoCrafter1</c> set to <paramref name="product"/> (when <paramref name="on"/>)
        /// with its own 8-slot inventory supplying that product, and a sign. Refused if a crafter already stands on that platform.
        /// </summary>
        public static BuildOutcome AddCrafter(string text, BuildPlan footprint, int platformIndex, string product, string label, bool on, IReadOnlyList<string>? localChestItems = null, Random? random = null)
        {
            var platform = footprint.Platforms.FirstOrDefault(p => p.Index == platformIndex);
            if (platform is null)
                return Failed($"Platform {platformIndex} is not part of this factory floor.");

            var world = Read(text);
            var half = footprint.Template.Spacing / 2;
            if (world.Objects.Any(o => o.GId.StartsWith("AutoCrafter", StringComparison.Ordinal) && Math.Abs(o.X - platform.X) <= half && Math.Abs(o.Z - platform.Z) <= half && Math.Abs(o.Y - platform.Y - OnFoundation) < 0.8))
                return Failed($"Platform {platformIndex} already has a crafter on it.");

            var foundationExists = world.Objects.Any(o => o.GId == "Foundation" && Math.Abs(o.X - platform.X) < half - 0.05 && Math.Abs(o.Z - platform.Z) < half - 0.05 && Math.Abs(o.Y - platform.Y) < 0.6);

            FactoryFloorCell cell;
            if (localChestItems is { Count: > 0 })
            {
                // Needs a local chest: the south corner for the crafter (away from the beacon), the north one for the chest — see ApplyFactory.
                var (rx, rz) = (footprint.Beacon.DirZ, -footprint.Beacon.DirX);
                var south = (X: platform.X + QuarterOffset * footprint.Beacon.DirX + QuarterOffset * rx, Z: platform.Z + QuarterOffset * footprint.Beacon.DirZ + QuarterOffset * rz);
                cell = new FactoryFloorCell(platform.Index, platform.X, platform.Z, true, foundationExists, null, 0, Array.Empty<string>(), null, null,
                    new[] { new FactoryQuarterTenant(product, south.X, south.Z, label, localChestItems, 0) });
            }
            else
            {
                cell = new FactoryFloorCell(platform.Index, platform.X, platform.Z, true, foundationExists, product, 0, Array.Empty<string>(), label);
            }

            var plan = new FactoryBuildPlan(footprint.Beacon, 0, platform.Y - footprint.Beacon.FoundationY, platform.Y, new[] { cell }, Array.Empty<FactoryProduct>(), Array.Empty<string>(), 0);

            return ApplyFactory(text, plan, on, random);
        }

        /// <summary>
        /// Adds one crafter, ad hoc, to a free corner of a platform that has no crafter on it at all (the same fallback as <see cref="PlanFactory"/>'s third pass): up
        /// to 4 can share one platform this way. Refused if the platform already has any crafter on it — quartering only ever starts from an empty platform; a
        /// platform with one centred crafter already needs a full BUILD FACTORY rebuild to convert (that can also move the existing crafter to make room).
        /// </summary>
        public static BuildOutcome AddQuarterCrafter(string text, BuildPlan footprint, int platformIndex, FactoryQuarterTenant tenant, bool on, Random? random = null)
        {
            var platform = footprint.Platforms.FirstOrDefault(p => p.Index == platformIndex);
            if (platform is null)
                return Failed($"Platform {platformIndex} is not part of this factory floor.");

            var world = Read(text);
            var half = footprint.Template.Spacing / 2;
            if (world.Objects.Any(o => o.GId.StartsWith("AutoCrafter", StringComparison.Ordinal) && Math.Abs(o.X - platform.X) <= half && Math.Abs(o.Z - platform.Z) <= half && Math.Abs(o.Y - platform.Y - OnFoundation) < 0.8))
                return Failed($"Platform {platformIndex} already has a crafter on it.");

            var cell = new FactoryFloorCell(platform.Index, platform.X, platform.Z, true,
                world.Objects.Any(o => o.GId == "Foundation" && Math.Abs(o.X - platform.X) < half - 0.05 && Math.Abs(o.Z - platform.Z) < half - 0.05 && Math.Abs(o.Y - platform.Y) < 0.6),
                null, 0, Array.Empty<string>(), null, null, new[] { tenant });
            var plan = new FactoryBuildPlan(footprint.Beacon, 0, platform.Y - footprint.Beacon.FoundationY, platform.Y, new[] { cell }, Array.Empty<FactoryProduct>(), Array.Empty<string>(), 0);

            return ApplyFactory(text, plan, on, random);
        }

        /// <summary>
        /// Removes one crafter and its sign (found within 1.3 m of it, just above it, the way <see cref="AddCrafter"/> and the factory builder place one) from the save,
        /// leaving the platform's foundation and everything else untouched. The result is checked the same way as any other removal.
        /// </summary>
        public static CrafterEditOutcome RemoveCrafter(string text, long crafterId)
        {
            var world = Read(text);
            var crafter = world.Objects.FirstOrDefault(o => o.Id == crafterId && o.GId.StartsWith("AutoCrafter", StringComparison.Ordinal));
            if (crafter is null)
                return new CrafterEditOutcome(null, new[] { "That crafter is not in the save; it may already have been removed." });

            var signs = world.Objects.Where(o => o.GId == "Sign"
                && Math.Sqrt(Math.Pow(o.X - crafter.X, 2) + Math.Pow(o.Z - crafter.Z, 2)) < 1.3
                && Math.Abs(o.Y - crafter.Y - CrafterSignUp) < 0.5).ToList();

            // Its own local chest, if it has one (the owner's fallback, or the quartering one): a Container1 a little further out than a sign, same height band.
            var localChests = world.Objects.Where(o => o.GId == "Container1"
                && Math.Sqrt(Math.Pow(o.X - crafter.X, 2) + Math.Pow(o.Z - crafter.Z, 2)) < 2.6
                && Math.Abs(o.Y - crafter.Y) < 0.2).ToList();

            var objectIds = new List<long> { crafter.Id }.Concat(signs.Select(s => s.Id)).Concat(localChests.Select(c => c.Id)).ToList();
            var inventoryIds = (crafter.LiId is { } li ? new[] { (long)li } : Array.Empty<long>())
                .Concat(localChests.Select(c => c.LiId).Where(id => id is not null).Select(id => (long)id!)).ToList();

            var (newText, problems) = RemoveByIds(text, objectIds, inventoryIds);
            return new CrafterEditOutcome(newText, problems);
        }

        /// <summary>
        /// The crafters standing on the factory floor, read from the save. What a crafter is for is what its inventory supplies (a crafter has no text label): the
        /// owner's own crafters carry it too. <c>Recipe</c> is what it is set to make now, null when it is off.
        /// </summary>
        public static IReadOnlyList<FactoryCrafter> ListCrafters(string text, BuildPlan footprint)
        {
            var world = Read(text);
            var half = footprint.Template.Spacing / 2;

            var supply = new Dictionary<long, string>();
            var recipes = new Dictionary<long, string>();
            foreach (Match m in RecordPattern.Matches(text))
            {
                var idMatch = Regex.Match(m.Value, @"^\{""id"":(\d+)");
                if (!idMatch.Success)
                    continue;

                var id = long.Parse(idMatch.Groups[1].Value);
                if (m.Value.Contains("\"woIds\"", StringComparison.Ordinal))
                {
                    var s = Regex.Match(m.Value, "\"supplyGrps\":\"([^\"]*)\"");
                    if (s.Success && s.Groups[1].Value.Length > 0 && !s.Groups[1].Value.Contains(','))
                        supply[id] = s.Groups[1].Value;
                }
                else if (m.Value.Contains("\"gId\":\"AutoCrafter", StringComparison.Ordinal))
                {
                    var r = Regex.Match(m.Value, "\"liGrps\":\"([^\"]*)\"");
                    if (r.Success && r.Groups[1].Value.Length > 0)
                        recipes[id] = r.Groups[1].Value;
                }
            }

            var result = new List<FactoryCrafter>();
            foreach (var o in world.Objects.Where(o => o.GId.StartsWith("AutoCrafter", StringComparison.Ordinal)))
            {
                var p = footprint.Platforms.FirstOrDefault(p => Math.Abs(o.X - p.X) <= half && Math.Abs(o.Z - p.Z) <= half && Math.Abs(o.Y - p.Y - OnFoundation) < 0.8);
                if (p is null)
                    continue;

                var product = o.LiId is { } li && supply.TryGetValue(li, out var sp) ? sp : (recipes.TryGetValue(o.Id, out var rc) ? rc : "");
                var recipe = recipes.TryGetValue(o.Id, out var set) ? set : null;
                result.Add(new FactoryCrafter(o.Id, product, recipe, o.X, o.Z, recipe is not null));
            }

            return result.OrderBy(c => c.Product, StringComparer.Ordinal).ThenBy(c => c.Id).ToList();
        }

        /// <summary>
        /// Switches factory crafters on or off by writing only their recipe: on sets <c>liGrps</c> to the recipe the crafter supplies (when that is a real recipe), off takes
        /// <c>liGrps</c> away. <paramref name="onlyId"/> limits it to one crafter. Nothing else in the save changes, and the result is checked against the original.
        /// </summary>
        public static FactoryToggleOutcome SetCrafters(string text, BuildPlan footprint, bool on, Func<string, bool> isRecipe, long? onlyId = null)
        {
            var crafters = ListCrafters(text, footprint).Where(c => onlyId is null || c.Id == onlyId).ToList();
            if (crafters.Count == 0)
                return new FactoryToggleOutcome(null, 0, new[] { "There is no crafter of the factory to switch." });

            var wanted = crafters.ToDictionary(c => c.Id);
            var records = ReadRecords(text).Where(r => !r.IsInventory && wanted.ContainsKey(r.Id)).OrderBy(r => r.Start).ToList();
            var edits = new List<(int Start, int Length, string Old, string New)>();

            foreach (var rec in records)
            {
                var c = wanted[rec.Id];
                var old = text.Substring(rec.Start, rec.Length);
                var without = Regex.Replace(old, ",\"liGrps\":\"[^\"]*\"", "");
                string next;

                if (!on)
                    next = without;
                else if (!string.IsNullOrEmpty(c.Product) && isRecipe(c.Product))
                    next = Regex.Replace(without, "(\"liId\":\\d+)", $"$1,\"liGrps\":\"{c.Product}\"", RegexOptions.None, TimeSpan.FromSeconds(1));
                else
                    continue; // labelled with something that is not a recipe: not ours to set

                if (!string.Equals(next, old, StringComparison.Ordinal))
                    edits.Add((rec.Start, rec.Length, old, next));
            }

            if (edits.Count == 0)
                return new FactoryToggleOutcome(null, 0, new[] { on ? "Every crafter is already on (or has no recipe label to set)." : "Every crafter is already off." });

            var result = new StringBuilder(text);
            for (var i = edits.Count - 1; i >= 0; i--)
                result.Remove(edits[i].Start, edits[i].Length).Insert(edits[i].Start, edits[i].New);

            var newText = result.ToString();

            // The proof: the records are the original ones, and only the edited ones differ, and only in their recipe field.
            var problems = new List<string>();
            var before = RecordPattern.Matches(text).Select(m => m.Value).ToList();
            var after = RecordPattern.Matches(newText).Select(m => m.Value).ToList();
            if (before.Count != after.Count)
                problems.Add("The number of records changed, so nothing was written.");
            else
            {
                var edited = edits.ToDictionary(e => e.Old, e => e.New, StringComparer.Ordinal);
                for (var i = 0; i < before.Count; i++)
                {
                    var expected = edited.TryGetValue(before[i], out var replaced) ? replaced : before[i];
                    if (!string.Equals(expected, after[i], StringComparison.Ordinal))
                    {
                        problems.Add("A record other than a crafter's recipe changed, so nothing was written.");
                        break;
                    }
                }
            }

            if (RecordPattern.Replace(text, "") != RecordPattern.Replace(newText, ""))
                problems.Add("The text between the records changed, so nothing was written.");

            return problems.Count > 0 ? new FactoryToggleOutcome(null, 0, problems) : new FactoryToggleOutcome(newText, edits.Count, Array.Empty<string>());
        }
    }
}
