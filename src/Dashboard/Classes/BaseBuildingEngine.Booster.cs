using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>A game id and what to call it: a machine tier, a seed to fill a machine with, a fuse. <paramref name="Slots"/> is a machine tier's own inventory size when it differs from
    /// the kind's (-1: read it from one in the save).</summary>
    public sealed record BoosterChoice(string GId, string Label, int? Slots = null);

    /// <summary>
    /// One kind of booster, as a preset of choices the page lets you change: the machine (and its tiers), what to fill it with, and the optimizers (none, T1 or T2, one per fuse).
    /// A beacon picks it by name (<paramref name="Key"/> or one of <paramref name="Aliases"/>, as a word of the beacon's name) or the page asks which to build.
    /// </summary>
    /// <param name="Machines">The machine, or its tiers; <paramref name="DefaultMachine"/> is the one chosen to begin with.</param>
    /// <param name="DefaultFuses">The fuse ids of the optimizers the preset starts with, one optimizer each.</param>
    /// <param name="Ring">The machines surround the optimizer(s) (which stand on the beacon) in a 3 by 3 block, instead of being stacked at the beacon with the optimizer beside them.
    /// Needed for machines the player has to open (a farm's larvae slots), which a stack would make unreachable.</param>
    /// <param name="MachineSlots">How many slots each machine's own inventory has when it needs one (a butterfly farm's three larvae slots); 0 when it has none (heaters, drills);
    /// -1 when the size is read from a machine of the kind already in the save.</param>
    /// <param name="Spacing">The default distance between neighbouring machines of a ring, in metres.</param>
    /// <param name="MachineInventorySettings">Extra fields written on each machine's own inventory record, as the save writes them (a beehive's drone settings: it supplies honey and bee larvae).</param>
    /// <param name="SupplyEverything">Each machine's inventory supplies everything the drones can carry (an Ecosystem makes every kind of larva).</param>
    /// <param name="PopulateOptions">What each machine's inventory can be filled with when it takes one thing (a tree spreader's seed); null when the build leaves it empty.</param>
    /// <param name="MachineExtra">Extra fields written on each machine's own record (a spreader's <c>"grwth":100</c>).</param>
    public sealed record BoosterKind(string Key, string Name, string[] Aliases, IReadOnlyList<BoosterChoice> Machines, int DefaultMachine, IReadOnlyList<string> DefaultFuses, int Count,
        bool Ring = false, int MachineSlots = 0, double Spacing = 0, string MachineInventorySettings = "", bool SupplyEverything = false,
        IReadOnlyList<BoosterChoice>? PopulateOptions = null, string? PopulateDefault = null, string MachineExtra = "")
    {
        public BoosterChoice DefaultMachineChoice => Machines[DefaultMachine];
        public string MachineGId => DefaultMachineChoice.GId;
        public string MachineName => DefaultMachineChoice.Label;
    }

    /// <summary>What the page may change on a preset. A null leaves the preset's own choice.</summary>
    /// <param name="Optimizers">Build optimizers at all (default yes).</param>
    /// <param name="Optimizer">Optimizer1 (T1, one fuse slot) or Optimizer2 (T2, three).</param>
    /// <param name="Fuses">The fuse ids, one optimizer each.</param>
    public sealed record BoosterOptions(string? Kind = null, double? Spacing = null, string? Machine = null, string? Populate = null, bool Optimizers = true, string? Optimizer = null,
        IReadOnlyList<string>? Fuses = null);

    /// <param name="Exists">An optimizer already stands where this one goes, so it is not added again.</param>
    /// <param name="GId">Optimizer1 or Optimizer2.</param>
    /// <param name="Slots">Its fuse slots (all filled).</param>
    public sealed record BoosterOptimizer(double X, double Z, BoosterChoice Fuse, bool Exists, string GId = "Optimizer2", int Slots = 3);

    /// <param name="MachinesFound">Machines of the kind already standing where they would go (a hand-placed one counts, and is not added again).</param>
    /// <param name="Spots">Where the machines still to be added go (a stack repeats one spot).</param>
    /// <param name="Slots">How many slots each new machine's inventory has (0 for none).</param>
    /// <param name="Spacing">The distance between a ring's neighbours (0 for a stack).</param>
    /// <param name="Machine">The machine being built (a tier of the kind's machine).</param>
    /// <param name="Populate">What each machine's inventory is filled with, or null.</param>
    public sealed record BoosterPlan(
        BuildBeacon? Beacon, BoosterKind? Kind, double X, double Y, double Z, int MachinesFound, IReadOnlyList<(double X, double Z)> Spots,
        IReadOnlyList<BoosterOptimizer> Optimizers, IReadOnlyList<string> Problems, double Spacing = 0, int Slots = 0, BoosterChoice? Machine = null, BoosterChoice? Populate = null)
    {
        public int MachinesToAdd => Spots.Count;

        /// <summary>There is something to build, and nothing in the way.</summary>
        public bool Ok => Beacon is not null && Kind is not null && Problems.Count == 0 && (MachinesToAdd > 0 || Optimizers.Any(o => !o.Exists));

        public bool AlreadyBuilt => Beacon is not null && Kind is not null && Problems.Count == 0 && MachinesToAdd == 0 && Optimizers.All(o => o.Exists);
    }

    /// <summary>What a booster build would write (or did). Nothing is returned when there is a problem.</summary>
    public sealed record BoosterOutcome(string? NewText, int Machines, int Optimizers, int Fuses, IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>
    /// The Base Building "Boosters": a beacon (named for the stat, or any name with the kind picked on the page) gets, where it stands, eight of that stat's best machine and
    /// optimizers holding its fuses. Found by the owner on 2026-10-09: machines can stand one on top of the other in a save and each still makes its share, so a stat that
    /// needs no one to open its machines (heat, pressure) is a stack at the beacon with the optimizer 3 m on. A machine that holds something the player must reach (a farm's
    /// larvae, a hive's honey, an ecosystem's larvae, a spreader's seed) goes in a 3 by 3 ring around the optimizer(s) on the beacon instead, as tight as the spacing says.
    /// The page offers a choice for each part: the machine's tier, what fills it, and whether to build optimizers, of which tier, holding which fuses.
    ///
    /// The ids are the game's, not the tiers' numbering: the T5 heater is <c>Heater5</c> but the T5 drill is <c>Drill4</c>, and the T1 tree spreader is <c>TreeSpreader0</c>. The beacon
    /// is left as it is. Nothing is checked for room, and neither is the ground. An optimizer reaches 15 m (<c>MachineOptimizer.range</c>) and each fuse boosts at most 5 machines,
    /// so a ring must keep its corners within <see cref="OptimizerRange"/> of the centre.
    /// </summary>
    public static partial class BaseBuildingEngine
    {
        /// <summary>The slots of the T2 optimizer (every one in the owner's saves has size 3); the T1 has one.</summary>
        public const int BoosterSlots = 3;

        /// <summary>How far from the stack the optimizer stands, along the way the beacon points.</summary>
        public const double BoosterOptimizerDistance = 3; // a stack's optimizer; a ring's stand on the beacon itself

        /// <summary>How far apart two optimizers standing together are.</summary>
        public const double OptimizerGap = 1.6;

        /// <summary>The reach of an optimizer in the game's own data (<c>MachineOptimizer.range</c>), in metres. The T2 may reach further; this is the default.</summary>
        public const double OptimizerRange = 15;

        public const string OptimizerGId = "Optimizer2";

        /// <summary>The optimizers: id and fuse slots (read from the owner's saves).</summary>
        public static readonly IReadOnlyList<(BoosterChoice Choice, int Slots)> OptimizerTiers = new[]
        {
            (new BoosterChoice("Optimizer1", "T1 optimizer (1 fuse slot)"), 1),
            (new BoosterChoice("Optimizer2", "T2 optimizer (3 fuse slots)"), 3)
        };

        /// <summary>The fuses an optimizer takes (<c>MachineOptimizer</c> knows these eight stats).</summary>
        public static readonly IReadOnlyList<BoosterChoice> Fuses = new[]
        {
            new BoosterChoice("FuseHeat1", "Heat"), new BoosterChoice("FusePressure1", "Pressure"), new BoosterChoice("FuseOxygen1", "Oxygen"), new BoosterChoice("FusePlants1", "Plants"),
            new BoosterChoice("FuseInsects1", "Insects"), new BoosterChoice("FuseAnimals1", "Animals"), new BoosterChoice("FuseEnergy1", "Energy"), new BoosterChoice("FusePurification1", "Purification")
        };

        /// <summary>The tree seeds, by the game's id (names from the game's data: the Redwood is <c>Tree15Seed</c>, "Redwo").</summary>
        public static readonly IReadOnlyList<BoosterChoice> TreeSeeds = new[]
        {
            new BoosterChoice("Tree0Seed", "Iterra"), new BoosterChoice("Tree1Seed", "Linifolia"), new BoosterChoice("Tree2Seed", "Aleatus"), new BoosterChoice("Tree3Seed", "Cernea"),
            new BoosterChoice("Tree4Seed", "Elegea"), new BoosterChoice("Tree5Seed", "Humelora"), new BoosterChoice("Tree6Seed", "Aemora"), new BoosterChoice("Tree7Seed", "Pleom"),
            new BoosterChoice("Tree8Seed", "Soleus"), new BoosterChoice("Tree9Seed", "Shreox"), new BoosterChoice("Tree10Seed", "Rosea"), new BoosterChoice("Tree11Seed", "Lillia"),
            new BoosterChoice("Tree12Seed", "Prunea"), new BoosterChoice("Tree13Seed", "Ruberu"), new BoosterChoice("Tree14Seed", "Malissea"), new BoosterChoice("Tree15Seed", "Redwood"),
            new BoosterChoice("Tree16Seed", "Pamelia"), new BoosterChoice("Tree17Seed", "Detoxo"), new BoosterChoice("Tree18Seed", "Brojo")
        };

        /// <summary>Frog eggs, by the game's id and name.</summary>
        public static readonly IReadOnlyList<BoosterChoice> FrogEggs = new[]
        {
            new BoosterChoice("Frog1Eggs", "Generic"), new BoosterChoice("Frog2Eggs", "Huli"), new BoosterChoice("Frog3Eggs", "Felicianna"), new BoosterChoice("Frog4Eggs", "Strabo"),
            new BoosterChoice("Frog5Eggs", "Trajuu"), new BoosterChoice("Frog6Eggs", "Aiolus"), new BoosterChoice("Frog7Eggs", "Afae"), new BoosterChoice("Frog8Eggs", "Cillus"),
            new BoosterChoice("Frog9Eggs", "Amedo"), new BoosterChoice("Frog10Eggs", "Kenjoss"), new BoosterChoice("Frog11Eggs", "Lavaum"), new BoosterChoice("Frog12Eggs", "Leglus"),
            new BoosterChoice("Frog13Eggs", "Jumi"), new BoosterChoice("Frog14Eggs", "Seren"), new BoosterChoice("Frog15Eggs", "Acuzzi"), new BoosterChoice("Frog16Eggs", "Toxifia"),
            new BoosterChoice("FrogGoldEggs", "Golden")
        };

        /// <summary>Butterfly larvae, by the game's id and name.</summary>
        public static readonly IReadOnlyList<BoosterChoice> ButterflyLarvae = new[]
        {
            new BoosterChoice("Butterfly1Larvae", "Azurae"), new BoosterChoice("Butterfly2Larvae", "Leani"), new BoosterChoice("Butterfly3Larvae", "Fensea"), new BoosterChoice("Butterfly4Larvae", "Galaxe"),
            new BoosterChoice("Butterfly5Larvae", "Abstreus"), new BoosterChoice("Butterfly6Larvae", "Empalio"), new BoosterChoice("Butterfly7Larvae", "Penga"), new BoosterChoice("Butterfly8Larvae", "Chevrone"),
            new BoosterChoice("Butterfly9Larvae", "Aemel"), new BoosterChoice("Butterfly10Larvae", "Liux"), new BoosterChoice("Butterfly11Larvae", "Nere"), new BoosterChoice("Butterfly12Larvae", "Lorpen"),
            new BoosterChoice("Butterfly13Larvae", "Fiorente"), new BoosterChoice("Butterfly14Larvae", "Alben"), new BoosterChoice("Butterfly15Larvae", "Futura"), new BoosterChoice("Butterfly16Larvae", "Imeo"),
            new BoosterChoice("Butterfly17Larvae", "Serena"), new BoosterChoice("Butterfly18Larvae", "Golden"), new BoosterChoice("Butterfly19Larvae", "Faleria"), new BoosterChoice("Butterfly20Larvae", "Oesbe"),
            new BoosterChoice("Butterfly21Larvae", "Lucia")
        };

        /// <summary>What a machine can be filled with, with "leave empty" first (the build fills every slot of each machine with the pick).</summary>
        private static IReadOnlyList<BoosterChoice> WithEmpty(IEnumerable<BoosterChoice> options) => new[] { new BoosterChoice("", "(leave empty)") }.Concat(options).ToList();

        private static BoosterChoice[] One(string gId, string label) => new[] { new BoosterChoice(gId, label) };

        public static readonly IReadOnlyList<BoosterKind> Boosters = new[]
        {
            new BoosterKind("Heat", "Heat", new[] { "heat", "heater", "hot", "warm" }, One("Heater5", "Heater T5"), 0, new[] { "FuseHeat1" }, 8),
            new BoosterKind("Pressure", "Pressure", new[] { "pressure", "drill" }, One("Drill4", "Drill T5"), 0, new[] { "FusePressure1" }, 8),

            // The farms are the T2 (ButterflyFarm3 exists but is not unlocked yet). The ring spacing is a guess from how close the owner placed his own: 6.57 to 7 m.
            // The T1 farm's slot count is in no save of the owner's, so it is read from one in the save (the T3 is locked).
            new BoosterKind("Butterflies", "Butterflies", new[] { "butterfl", "moth" },
                new[] { new BoosterChoice("ButterflyFarm1", "Butterfly Farm T1", -1), new BoosterChoice("ButterflyFarm2", "Butterfly Farm T2", 3) }, 1, new[] { "FuseInsects1" }, 8, Ring: true,
                MachineSlots: 3, Spacing: 6.6, PopulateOptions: WithEmpty(ButterflyLarvae), PopulateDefault: ""),

            // The owner's own beehives (T2, four slots) all supplied honey and bee larvae to the drones; new ones are written the same way. Their closest placement was 6.96 m,
            // but he chose 3.3 m (half the butterflies' 6.6) as the default on 2026-10-09.
            new BoosterKind("Bees", "Bees", new[] { "bee", "hive", "honey" },
                new[] { new BoosterChoice("Beehive1", "Beehive T1", -1), new BoosterChoice("Beehive2", "Beehive T2", 4) }, 1, new[] { "FuseInsects1" }, 8, Ring: true, MachineSlots: 4, Spacing: 3.3,
                MachineInventorySettings: ",\"demandGrps\":\"\",\"supplyGrps\":\"honey,Bee1Larvae\",\"priority\":0"),

            // An ecosystem boosts plants and insects, so it gets two optimizers on the beacon (plants fuses, insect fuses). Each holds four larvae-type items and the owner has it
            // supply everything, since it makes every kind of larva. The spacing keeps the ring's corners inside an optimizer's 15 m reach (10.5 x 1.41 = 14.8); its real size is
            // not in any save (the owner's old ones stood 21 m apart or more), so this is the page's setting to tune.
            new BoosterKind("Ecosystem", "Ecosystem", new[] { "eco" }, One("Ecosystem1", "Ecosystem"), 0, new[] { "FusePlants1", "FuseInsects1" }, 8, Ring: true, MachineSlots: 4,
                Spacing: 10.5, SupplyEverything: true),

            // The amphibian farm boosts plants and animals: two optimizers. It is locked in the owner's game (T1 only, "AmphibiansFarm1"), so no save has one: its slot count is
            // read from the first one the save has.
            new BoosterKind("Frogs", "Frog Farm", new[] { "frog", "toad", "amphib" }, One("AmphibiansFarm1", "Amphibian Farm"), 0, new[] { "FusePlants1", "FuseAnimals1" }, 8, Ring: true,
                MachineSlots: -1, Spacing: 6.6, PopulateOptions: WithEmpty(FrogEggs), PopulateDefault: ""),

            // A tree spreader boosts oxygen and plants: two optimizers. Its tiers are TreeSpreader0, 1 and 2 (T1 to T3); each holds one tree seed (inventory size 1), which a placed one
            // shows as "grwth":100 (52 of the 54 in the owner's old base). Their closest placement was 7.25 m. The owner wants the T3, filled with the Redwood seed.
            new BoosterKind("Trees", "Tree Spreader Farm", new[] { "tree", "spreader", "redwood", "forest" },
                new[] { new BoosterChoice("TreeSpreader0", "Tree Spreader T1"), new BoosterChoice("TreeSpreader1", "Tree Spreader T2"), new BoosterChoice("TreeSpreader2", "Tree Spreader T3") }, 2,
                new[] { "FuseOxygen1", "FusePlants1" }, 8, Ring: true, MachineSlots: 1, Spacing: 7.3, PopulateOptions: WithEmpty(TreeSeeds), PopulateDefault: "Tree15Seed", MachineExtra: ",\"grwth\":100")
        };

        /// <summary>The 3 by 3 block's eight places around its centre, in steps from it (the centre is the optimizers').</summary>
        private static readonly (int Dx, int Dz)[] RingCells = { (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1) };

        /// <summary>The booster a beacon's name asks for, or null when it names none. Forgiving: the name is split into words and any word that starts with a kind's name or one of
        /// its aliases ("Heat", "heater north", "Frog Farm", "frogs", "my bees") counts; the longest match wins.</summary>
        public static BoosterKind? BoosterFor(string beaconText)
        {
            var words = Regex.Split((beaconText ?? "").ToLowerInvariant(), "[^a-z]+").Where(w => w.Length >= 3).ToList();
            BoosterKind? best = null;
            var bestLength = 0;

            foreach (var kind in Boosters)
            {
                foreach (var alias in kind.Aliases.Append(kind.Key.ToLowerInvariant()))
                {
                    if (alias.Length > bestLength && words.Any(w => w.StartsWith(alias, StringComparison.Ordinal)))
                    {
                        best = kind;
                        bestLength = alias.Length;
                    }
                }
            }

            return best;
        }

        public static BoosterKind? BoosterByKey(string? key) =>
            string.IsNullOrWhiteSpace(key) ? null : Boosters.FirstOrDefault(k => k.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        /// <param name="options">What the page chose (any beacon name will do when it names the kind); null for the name's own kind with the preset's choices.</param>
        public static BoosterPlan PlanBooster(string text, long beaconId, BoosterOptions? options = null)
        {
            options ??= new BoosterOptions();

            BoosterPlan Problem(string message, BuildBeacon? beacon = null, BoosterKind? kind = null) =>
                new(beacon, kind, 0, 0, 0, 0, Array.Empty<(double, double)>(), Array.Empty<BoosterOptimizer>(), new[] { message });

            var world = Read(text);
            var beacon = Beacons(world).FirstOrDefault(b => b.Id == beaconId);
            if (beacon is null)
                return Problem("That beacon is not in the save.");

            var kind = BoosterByKey(options.Kind) ?? BoosterFor(beacon.Text);
            if (kind is null)
                return Problem("Pick which booster to build at this beacon.", beacon);

            var machine = kind.Machines.FirstOrDefault(m => m.GId == options.Machine) ?? kind.DefaultMachineChoice;
            var wanted = options.Populate ?? kind.PopulateDefault ?? "";
            var populate = wanted.Length == 0 ? null : kind.PopulateOptions?.FirstOrDefault(p => p.GId == wanted);

            bool At(Obj o, string gId, double x, double z) =>
                o.GId == gId && Math.Abs(o.X - x) < 0.3 && Math.Abs(o.Z - z) < 0.3 && Math.Abs(o.Y - beacon.Y) < 0.5;

            // How big each machine's own inventory is: a fixed number, or the size of one of the kind already in the save.
            var slots = machine.Slots ?? kind.MachineSlots;
            if (slots < 0)
            {
                var sample = world.Objects.FirstOrDefault(o => o.GId == machine.GId && o.LiId is { } li && world.InventorySizes.ContainsKey(li));
                if (sample is null)
                    return Problem($"There is no {machine.Label} in the save to copy its inventory size from. Place one by hand, save, and try again.", beacon, kind);

                slots = world.InventorySizes[sample.LiId!.Value];
            }

            // The optimizers: one per fuse, of the chosen tier (or none).
            var tier = OptimizerTiers.FirstOrDefault(t => t.Choice.GId == options.Optimizer);
            if (tier.Choice is null)
                tier = OptimizerTiers.First(t => t.Choice.GId == OptimizerGId);
            var fuseIds = options.Fuses ?? kind.DefaultFuses;
            var fuses = fuseIds.Select(id => Fuses.FirstOrDefault(f => f.GId == id)).Where(f => f is not null).Select(f => f!).Distinct().ToList();
            if (!options.Optimizers)
                fuses.Clear();

            var spots = new List<(double X, double Z)>();
            var optimizers = new List<BoosterOptimizer>();
            double step = 0;
            int found;

            if (kind.Ring)
            {
                // The optimizers stand on the beacon (side by side when there are several); the machines in the eight places around, as tight as the spacing says. A machine
                // already in a place stays.
                step = Math.Clamp(options.Spacing ?? kind.Spacing, 2, 30);
                found = 0;
                foreach (var (dx, dz) in RingCells)
                {
                    double x = beacon.X + dx * step, z = beacon.Z + dz * step;
                    if (world.Objects.Any(o => At(o, machine.GId, x, z)))
                        found++;
                    else
                        spots.Add((x, z));
                }

                for (var i = 0; i < fuses.Count; i++)
                {
                    var z = beacon.Z + (i - (fuses.Count - 1) / 2.0) * OptimizerGap;
                    optimizers.Add(new BoosterOptimizer(beacon.X, z, fuses[i], world.Objects.Any(o => At(o, tier.Choice.GId, beacon.X, z)), tier.Choice.GId, tier.Slots));
                }
            }
            else
            {
                // The stack stands exactly where the beacon does; the optimizers a few metres on, along the way the beacon points (side by side across it when there are several).
                double bx = beacon.X + beacon.DirX * BoosterOptimizerDistance, bz = beacon.Z + beacon.DirZ * BoosterOptimizerDistance;
                double px = Math.Abs(beacon.DirZ), pz = Math.Abs(beacon.DirX); // across the way it points
                found = world.Objects.Count(o => At(o, machine.GId, beacon.X, beacon.Z));
                for (var i = found; i < kind.Count; i++)
                    spots.Add((beacon.X, beacon.Z));

                for (var i = 0; i < fuses.Count; i++)
                {
                    var shift = (i - (fuses.Count - 1) / 2.0) * OptimizerGap;
                    double ox = bx + px * shift, oz = bz + pz * shift;
                    optimizers.Add(new BoosterOptimizer(ox, oz, fuses[i], world.Objects.Any(o => At(o, tier.Choice.GId, ox, oz)), tier.Choice.GId, tier.Slots));
                }
            }

            return new BoosterPlan(beacon, kind, beacon.X, beacon.Y, beacon.Z, found, spots, optimizers, Array.Empty<string>(), step, slots, machine, populate);
        }

        /// <summary>
        /// Writes a booster plan into the text of a save: the missing machines (each with the inventory it needs, filled with the chosen item), and an optimizer with its inventory of
        /// fuses for each that is not standing already. New records go in front of the beacon's own record, and the inventories in front of the last one, like the other builds.
        /// Nothing else changes: the proof is that taking out what was added gives back the original exactly. Any problem means no new text at all.
        /// </summary>
        public static BoosterOutcome ApplyBooster(string text, BoosterPlan plan, Random? random = null)
        {
            random ??= Rng;

            BoosterOutcome Fail(string message) => new(null, 0, 0, 0, new[] { message });

            if (plan.Beacon is null || plan.Kind is null || plan.Machine is null || plan.Problems.Count > 0)
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

            // The drone settings each new machine's inventory gets: everything (the game's own list as this save wrote it, else the dashboard's), or the kind's own.
            var settings = plan.Kind.MachineInventorySettings;
            if (plan.Kind.SupplyEverything)
                settings = ",\"demandGrps\":\"\",\"supplyGrps\":\"" + string.Join(",", LearnEverythingFrom(inventories.Select(r => r.Raw))) + "\",\"priority\":0";

            var newObjects = new List<string>();
            var newInventories = new List<string>();
            var objectIds = new List<long>();
            var inventoryIds = new List<long>();
            var nextInventory = inventories.Max(r => r.Id);

            foreach (var (sx, sz) in plan.Spots)
            {
                var id = NewObjectId();
                objectIds.Add(id);

                // A machine the player opens (a farm's larvae slots) has an inventory of its own, empty to begin with or filled with the chosen item (a spreader's seed).
                var link = "";
                if (plan.Slots > 0)
                {
                    var held = new List<long>();
                    if (plan.Populate is not null)
                    {
                        for (var i = 0; i < plan.Slots; i++)
                        {
                            var itemId = NewObjectId();
                            objectIds.Add(itemId);
                            held.Add(itemId);
                            newObjects.Add($"{{\"id\":{itemId},\"gId\":\"{plan.Populate.GId}\"}}");
                        }
                    }

                    var inventoryId = ++nextInventory;
                    inventoryIds.Add(inventoryId);
                    newInventories.Add($"{{\"id\":{inventoryId},\"woIds\":\"{string.Join(",", held)}\",\"size\":{plan.Slots}{settings}}}");
                    link = $",\"liId\":{inventoryId}";
                }

                newObjects.Add($"{{\"id\":{id},\"gId\":\"{plan.Machine.GId}\"{link},\"pos\":\"{Pos(sx, plan.Y, sz)}\",\"rot\":\"0,0,0,1\",\"planet\":{planet}{plan.Kind.MachineExtra}}}");
            }

            var optimizerCount = 0;
            var fuseCount = 0;
            foreach (var optimizer in plan.Optimizers.Where(o => !o.Exists))
            {
                var held = new List<long>();
                for (var i = 0; i < optimizer.Slots; i++)
                {
                    var fuseId = NewObjectId();
                    objectIds.Add(fuseId);
                    held.Add(fuseId);
                    newObjects.Add($"{{\"id\":{fuseId},\"gId\":\"{optimizer.Fuse.GId}\"}}");
                }

                var inventoryId = ++nextInventory;
                inventoryIds.Add(inventoryId);
                newInventories.Add($"{{\"id\":{inventoryId},\"woIds\":\"{string.Join(",", held)}\",\"size\":{optimizer.Slots}}}");

                var optimizerId = NewObjectId();
                objectIds.Add(optimizerId);
                newObjects.Add($"{{\"id\":{optimizerId},\"gId\":\"{optimizer.GId}\",\"liId\":{inventoryId},\"pos\":\"{Pos(optimizer.X, plan.Y, optimizer.Z)}\",\"rot\":\"0,0,0,1\",\"planet\":{planet}}}");
                optimizerCount++;
                fuseCount += optimizer.Slots;
            }

            var objectText = string.Concat(newObjects.Select(r => r + "|" + eol));
            var inventoryText = string.Concat(newInventories.Select(r => r + "|" + eol));
            var lastInventory = inventories.OrderBy(r => r.Start).Last();

            // The inventory insertion is later in the text, so it goes in first and the earlier one keeps its place.
            var newText = text.Insert(lastInventory.Start, inventoryText).Insert(beacon.Start, objectText);

            var problems = VerifyBuild(text, newText, objectText, inventoryText, objectIds, inventoryIds, inventoryIds);
            return problems.Count > 0 ? new BoosterOutcome(null, 0, 0, 0, problems) : new BoosterOutcome(newText, plan.MachinesToAdd, optimizerCount, fuseCount, Array.Empty<string>());
        }
    }
}
