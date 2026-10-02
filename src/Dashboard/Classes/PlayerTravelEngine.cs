using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>One warehouse beacon ("All", "Everything" or "Warehouse") the player could be moved to, on its own planet.</summary>
    public sealed record TravelDestination(int PlanetHash, string? PlanetName, long BeaconId, string BeaconText, double X, double Y, double Z);

    /// <summary>One planet this save knows about, for the Travel tab's planet picker (whether or not it has a warehouse beacon).</summary>
    public sealed record TravelPlanet(int PlanetHash, string PlanetId);

    public sealed record TravelOutcome(string? NewText, string? Error)
    {
        public bool Changed => NewText != null;
        public bool Failed => Error != null;
    }

    /// <summary>
    /// The Cheats page's Travel tab: moves the player, in the text of a save, to just off a warehouse beacon on the
    /// planet picked — the only "teleport" this offers for now (a base-to-base one, same idea but picking from the
    /// bases the dashboard already lists, may follow later), and only ever to a planet the owner has already set a
    /// warehouse up on, so the destination is always somewhere real and already visited (never a planet with no
    /// state of its own in the save to land on). Edits exactly the player's own record: its position and its current
    /// planet; nothing else (the player's inventory, stats and everything else carry over untouched, the way walking
    /// there in the game would leave them). Same proof obligation as every other Cheats edit: undoing it must give
    /// back the original text exactly, or nothing is returned at all.
    /// </summary>
    public static class PlayerTravelEngine
    {
        private static readonly string[] WarehouseWords = { "all", "everything", "warehouse" };

        private static readonly Regex RecordPattern = new(
            @"\{(?:[^{}""]|""(?:[^""\\]|\\.)*"")*\}",
            RegexOptions.Compiled);

        private static readonly Regex PlayerPositionValue = new(@"(""playerPosition"":"")[^""]*("")", RegexOptions.Compiled);
        private static readonly Regex PlanetIdValue = new(@"(""planetId"":"")[^""]*("")", RegexOptions.Compiled);

        // Every planet's own terraformation-state record ({"planetId":"Humble",...}) carries its name but has no "id" of
        // its own, so it never turns up in Scan()'s id-keyed records; this reads "planetId" straight off the raw text
        // instead, wherever it appears (that record, the player's own, or anywhere else that ever gets one).
        private static readonly Regex PlanetIdField = new(@"""planetId"":""([^""]*)""", RegexOptions.Compiled);

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed record Rec(int Start, int Length, string Raw, long Id, string? GId, string? Text, string? Pos, int Planet, string? PlayerPosition);

        /// <summary>Every planet this save has state for, named, whether or not it has a warehouse beacon — for the Travel
        /// tab's planet picker, which comes first: a beacon (if that planet has one) or a typed-in spot both come after.</summary>
        public static IReadOnlyList<TravelPlanet> Planets(string text) =>
            HashPlanetIds(text).Select(kv => new TravelPlanet(kv.Key, kv.Value)).OrderBy(p => p.PlanetId, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Every warehouse beacon in the save, one per planet (the first one found there), named when the save itself
        /// says what that planet is called (see <see cref="HashPlanetIds"/>). Empty if the save cannot be read.</summary>
        public static IReadOnlyList<TravelDestination> Destinations(string text)
        {
            var records = Scan(text, out var problems);
            if (problems.Count > 0)
                return Array.Empty<TravelDestination>();

            var names = HashPlanetIds(text);
            var result = new List<TravelDestination>();

            foreach (var b in records)
            {
                if (b.GId != "Beacon" || b.Pos is null || string.IsNullOrWhiteSpace(b.Text))
                    continue;

                var word = new string(b.Text.Where(char.IsLetter).ToArray()).ToLowerInvariant();
                if (!WarehouseWords.Contains(word))
                    continue;

                if (!TryParsePos(b.Pos, out var x, out var y, out var z))
                    continue;

                result.Add(new TravelDestination(b.Planet, names.GetValueOrDefault(b.Planet), b.Id, b.Text.Trim(), x, y, z));
            }

            return result
                .GroupBy(d => d.PlanetHash)
                .Select(g => g.First())
                .OrderBy(d => d.PlanetName ?? d.PlanetHash.ToString(Inv), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The warehouse beacon on one specific planet, if it has one — for the Travel tab's "no location: use the
        /// beacon" default. Null when that planet has none (or is not in the save).</summary>
        public static TravelDestination? BeaconOn(string text, int planetHash) =>
            Destinations(text).FirstOrDefault(d => d.PlanetHash == planetHash);

        /// <summary>
        /// Moves the save's one player record to just off the chosen beacon (2 m to its south and east, 1 m up, so
        /// nothing lands inside the floor or on top of the beacon itself) and onto that beacon's planet. Fails if the
        /// beacon or the player record is not found, or if the beacon's planet cannot be matched to a name elsewhere
        /// in the save (should not happen for a planet the owner has actually visited).
        /// </summary>
        public static TravelOutcome MoveTo(string text, long beaconId)
        {
            var records = Scan(text, out var problems);
            if (problems.Count > 0)
                return new TravelOutcome(null, string.Join(" ", problems));

            var beacon = records.FirstOrDefault(r => r.Id == beaconId && r.GId == "Beacon");
            if (beacon?.Pos is null || !TryParsePos(beacon.Pos, out var bx, out var by, out var bz))
                return new TravelOutcome(null, "That beacon is not in the save.");

            var names = HashPlanetIds(text);
            if (!names.TryGetValue(beacon.Planet, out var planetId))
                return new TravelOutcome(null, "Could not work out which planet that beacon is on.");

            // 2 m to its south and east, 1 m up, so nothing lands inside the floor or on top of the beacon itself.
            return MoveToPosition(text, bx + 2, by + 1, bz + 2, planetId);
        }

        /// <summary>
        /// The same move, to an exact spot instead of a beacon — not offered from the Travel tab yet (nothing there
        /// picks an arbitrary point), but the one <see cref="MoveTo"/> itself builds on, and already usable directly
        /// for a one-off ("go here") without waiting on more UI.
        /// </summary>
        public static TravelOutcome MoveToPosition(string text, double x, double y, double z, string planetId)
        {
            var records = Scan(text, out var problems);
            if (problems.Count > 0)
                return new TravelOutcome(null, string.Join(" ", problems));

            var player = records.FirstOrDefault(r => r.PlayerPosition is not null);
            if (player is null)
                return new TravelOutcome(null, "No player record was found in the save.");

            var newPos = string.Join(",", Num(x), Num(y), Num(z));

            var newRaw = PlayerPositionValue.Replace(player.Raw, m => m.Groups[1].Value + newPos + m.Groups[2].Value, 1);
            newRaw = PlanetIdValue.IsMatch(newRaw)
                ? PlanetIdValue.Replace(newRaw, m => m.Groups[1].Value + planetId + m.Groups[2].Value, 1)
                : newRaw;

            if (string.Equals(newRaw, player.Raw, StringComparison.Ordinal))
                return new TravelOutcome(null, "Already exactly there; nothing to change.");

            var result = new StringBuilder(text);
            result.Remove(player.Start, player.Length);
            result.Insert(player.Start, newRaw);
            var newText = result.ToString();

            var check = Verify(text, newText, player.Raw, newRaw);
            return check is null ? new TravelOutcome(newText, null) : new TravelOutcome(null, check);
        }

        // ------------------------------------------------------------------

        private static bool TryParsePos(string pos, out double x, out double y, out double z)
        {
            x = y = z = 0;
            var parts = pos.Split(',');
            return parts.Length == 3
                && double.TryParse(parts[0], NumberStyles.Float, Inv, out x)
                && double.TryParse(parts[1], NumberStyles.Float, Inv, out y)
                && double.TryParse(parts[2], NumberStyles.Float, Inv, out z);
        }

        private static string Num(double v) => v.ToString("0.###", Inv);

        /// <summary>
        /// Hash to planet id ("Prime", "Humble", ...), worked out from every distinct <c>planetId</c> string anywhere
        /// in this save (the player's own, and each planet's own terraformation-state record) through the game's own
        /// string hash — the only way a beacon's numeric "planet" can be matched to a name, since the save never
        /// carries both together on the same record. Ported from the game's own <c>GetStableHashCode()</c>, confirmed
        /// byte-for-byte: "Prime" to -1140328421, "Humble" to -486276833.
        /// </summary>
        private static Dictionary<int, string> HashPlanetIds(string text)
        {
            var map = new Dictionary<int, string>();
            foreach (Match m in PlanetIdField.Matches(text))
            {
                var name = m.Groups[1].Value;
                if (name.Length > 0)
                    map.TryAdd(StableHash(name), name);
            }

            return map;
        }

        internal static int StableHash(string str)
        {
            int num = 5381, num2 = num;
            for (var i = 0; i < str.Length && str[i] != 0; i += 2)
            {
                num = ((num << 5) + num) ^ str[i];
                if (i == str.Length - 1 || str[i + 1] == '\0')
                    break;

                num2 = ((num2 << 5) + num2) ^ str[i + 1];
            }

            return num + num2 * 1566083941;
        }

        private static List<Rec> Scan(string text, out List<string> problems)
        {
            problems = new List<string>();
            var records = new List<Rec>();

            foreach (Match match in RecordPattern.Matches(text))
            {
                try
                {
                    using var doc = JsonDocument.Parse(match.Value);
                    var root = doc.RootElement;

                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
                        continue;

                    string? Str(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    int? Int(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : null;

                    records.Add(new Rec(match.Index, match.Length, match.Value, id, Str("gId"), Str("text"), Str("pos"), Int("planet") ?? 0, Str("playerPosition")));
                }
                catch (JsonException ex)
                {
                    problems.Add($"A record at position {match.Index} is not valid JSON: {ex.Message}");
                }
            }

            return records;
        }

        /// <summary>Checks the edited text against the original before anyone is allowed to write it.</summary>
        private static string? Verify(string before, string after, string originalRaw, string newRaw)
        {
            var at = after.IndexOf(newRaw, StringComparison.Ordinal);
            if (at < 0)
                return "The edited player record was not found in the result.";

            var restored = after.Remove(at, newRaw.Length).Insert(at, originalRaw);
            if (!string.Equals(restored, before, StringComparison.Ordinal))
                return "Undoing the edit does not give back the original save, so something else changed. Nothing was written.";

            var records = Scan(after, out var problems);
            if (problems.Count > 0)
                return string.Join(" ", problems);

            var players = records.Count(r => r.PlayerPosition is not null);
            if (players != 1)
                return $"There should be exactly one player record after the edit; found {players}.";

            return null;
        }
    }
}
