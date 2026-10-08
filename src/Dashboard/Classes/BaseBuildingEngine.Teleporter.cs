using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// A teleporter site read from a beacon: the one platform behind the beacon (the way it points, like the warehouse's first platform) with a Teleporter1 on it.
    /// <paramref name="Beacon"/> is null when the beacon could not be used (see <paramref name="Problems"/>).
    /// </summary>
    /// <param name="FoundationExists">A foundation already stands where the platform goes, so only the teleporter is added.</param>
    /// <param name="Conflicts">Things already standing on that platform's square that a build would collide with.</param>
    public sealed record TeleportPlan(
        BuildBeacon? Beacon, double PlatformX, double PlatformY, double PlatformZ, bool FoundationExists,
        double X, double Y, double Z, string Rot, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Problems)
    {
        public bool Ok => Beacon is not null && Problems.Count == 0 && Conflicts.Count == 0;
    }

    /// <summary>What a teleporter build would write (or did). Nothing is returned when there is a problem.</summary>
    public sealed record TeleportOutcome(string? NewText, int Foundations, int Teleporters, IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>
    /// The Base Building "Teleporter" template: a foundation with a beacon on it (named for what it is, like <c>Teleport</c>), and behind it, the way the
    /// beacon points, one more foundation carrying a teleporter turned to face away from the beacon.
    ///
    /// What a save shows of a Teleporter1 (13 in the owner's Custom-2, 2026-10-08): it stands 2.519 m above its foundation, like a chest, within about a
    /// tenth of a metre of the foundation's centre (then moved <see cref="TeleporterNudge"/> along the way it faces, see there); its name is just the record's <c>text</c>; and its <c>set</c> is its number in the game's list, which
    /// the game gives itself (one more than the highest) when a teleporter spawns with none, so a new one is written without it.
    /// </summary>
    public static partial class BaseBuildingEngine
    {
        /// <summary>The distance between the centres of two foundations along a row.</summary>
        public const double PlatformSpacing = 6;

        /// <summary>The longest name a teleporter is given.</summary>
        public const int MaxTeleporterLabel = 40;

        /// <summary>
        /// How far the teleporter is moved from the platform's centre along the way it faces. Its back overhangs by about a metre, so a teleporter put dead
        /// centre facing away from the beacon stands partly over the beacon's own foundation (the owner saw this in the game, 2026-10-08).
        /// </summary>
        public const double TeleporterNudge = 1.0;

        public const string TeleporterGId = "Teleporter1";

        /// <param name="faceAway">The teleporter faces away from the beacon (the way it points); false turns it to face the beacon.</param>
        public static TeleportPlan PlanTeleporter(string text, long beaconId, bool faceAway, Func<string, bool> isBuilding)
        {
            TeleportPlan Problem(string message) => new(null, 0, 0, 0, false, 0, 0, 0, "0,0,0,1", Array.Empty<string>(), new[] { message });

            var world = Read(text);
            var beacon = Beacons(world).FirstOrDefault(b => b.Id == beaconId);
            if (beacon is null)
                return Problem("That beacon is not in the save.");

            if (beacon.FoundationId is null)
                return Problem("The beacon is not standing on a foundation.");

            var (dx, dz) = (beacon.DirX, beacon.DirZ);
            double fx = beacon.FoundationX + dx * PlatformSpacing, fy = beacon.FoundationY, fz = beacon.FoundationZ + dz * PlatformSpacing;
            var half = PlatformSpacing / 2;

            bool IsThatFoundation(Obj o) => o.GId == "Foundation" && Math.Abs(o.X - fx) < 0.6 && Math.Abs(o.Z - fz) < 0.6 && Math.Abs(o.Y - fy) < 0.6;

            var exists = world.Objects.Any(IsThatFoundation);

            var conflicts = world.Objects
                .Where(o => o.Id != beacon.Id && isBuilding(o.GId) && !IsThatFoundation(o)
                            && Math.Abs(o.X - fx) < half - 0.05 && Math.Abs(o.Z - fz) < half - 0.05
                            && o.Y > fy - 1 && o.Y < fy + 8)
                .Take(4)
                .Select(o => $"{o.GId} at ({o.X:0.#}, {o.Z:0.#})")
                .ToList();

            // Unity's forward for a yaw of t degrees is (sin t, cos t) in (x, z), so facing along (dx, dz) is a yaw of atan2(dx, dz).
            var sign = faceAway ? 1 : -1;
            var yaw = (int)Math.Round(Math.Atan2(sign * dx, sign * dz) * 180 / Math.PI);

            // Along the way it faces: further from the beacon when it faces away, toward the beacon when it faces it.
            double tx = fx + sign * dx * TeleporterNudge, tz = fz + sign * dz * TeleporterNudge;

            return new TeleportPlan(beacon, fx, fy, fz, exists, tx, Math.Round(fy + OnFoundation, 3), tz, TurnRot("0,0,0,1", yaw), conflicts, Array.Empty<string>());
        }

        /// <summary>
        /// Writes a teleporter plan into the text of a save: a foundation if there is none yet, and a Teleporter1 on it, named <paramref name="label"/> (nothing when blank).
        /// New records go in front of the beacon's own record, like the other builds. Nothing else changes: the proof is that taking out what was added gives
        /// back the original exactly. Any problem means no new text at all.
        /// </summary>
        public static TeleportOutcome ApplyTeleporter(string text, TeleportPlan plan, string? label, Random? random = null)
        {
            random ??= Rng;

            TeleportOutcome Fail(string message) => new(null, 0, 0, new[] { message });

            if (plan.Beacon is null || plan.Problems.Count > 0)
                return new TeleportOutcome(null, 0, 0, plan.Problems.Count > 0 ? plan.Problems : new[] { "There is no beacon to build from." });

            if (plan.Conflicts.Count > 0)
                return Fail($"The platform is not clear ({string.Join(", ", plan.Conflicts)}), so nothing was built.");

            label = label?.Trim();
            if (label is { Length: > 0 })
            {
                if (label.Length > MaxTeleporterLabel)
                    return Fail($"The name is longer than {MaxTeleporterLabel} characters.");

                if (label.Any(c => c is '"' or '\\' or '|' || char.IsControl(c)))
                    return Fail("The name cannot hold quotes, backslashes, bars or line breaks.");
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
                    return Fail($"A record at position {match.Index} is not valid JSON: {ex.Message}");
                }
            }

            var beacon = records.FirstOrDefault(r => !r.IsInventory && r.Id == plan.Beacon.Id && r.Raw.Contains("\"gId\":\"Beacon\"", StringComparison.Ordinal));
            if (beacon.Raw is null)
                return Fail("The beacon could not be found in the text, so nothing was built.");

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
            var objectIds = new List<long>();
            var foundations = 0;

            if (!plan.FoundationExists)
            {
                var foundationId = NewObjectId();
                objectIds.Add(foundationId);
                newObjects.Add($"{{\"id\":{foundationId},\"gId\":\"Foundation\",\"pos\":\"{Pos(plan.PlatformX, plan.PlatformY, plan.PlatformZ)}\",\"rot\":\"0,0,0,1\",\"planet\":{planet}}}");
                foundations++;
            }

            var teleporterId = NewObjectId();
            objectIds.Add(teleporterId);
            var name = label is { Length: > 0 } ? $",\"text\":\"{label}\"" : "";
            newObjects.Add($"{{\"id\":{teleporterId},\"gId\":\"{TeleporterGId}\",\"pos\":\"{Pos(plan.X, plan.Y, plan.Z)}\",\"rot\":\"{plan.Rot}\",\"planet\":{planet}{name}}}");

            var objectText = string.Concat(newObjects.Select(r => r + "|" + eol));
            var newText = text.Insert(beacon.Start, objectText);

            var problems = VerifyBuild(text, newText, objectText, "", objectIds, new List<long>(), new List<long>());
            return problems.Count > 0 ? new TeleportOutcome(null, 0, 0, problems) : new TeleportOutcome(newText, foundations, 1, Array.Empty<string>());
        }
    }
}
