using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>One item sitting in a storage unit: its object id and what it is.</summary>
    public sealed record StoredItem(long Id, string GId);

    /// <summary>
    /// A place that holds items, as the Storage tab shows it: a container, a locker, a vault, a refrigerator, a player's backpack, a vehicle trunk, an escape storage
    /// unit, or one of the rocket storages. <see cref="Size"/> is its number of slots; the items are in slot order.
    /// </summary>
    public sealed record StorageUnit(long InventoryId, string Kind, string GId, string Label, string Position, int PlanetHash, int Size, IReadOnlyList<StoredItem> Items, bool NearWarehouse = false);

    /// <summary>The storage units of a save, and the planet the player is registered on (its hash and name; the tab shows only that planet's units). Both are empty when the save does not say.</summary>
    public sealed record StorageSnapshot(IReadOnlyList<StorageUnit> Units, int PlanetHash, string? PlanetName, string? Error, IReadOnlyList<int>? BeaconPlanets = null,
        IReadOnlyDictionary<int, string>? PlanetNamesByHash = null)
    {
        /// <summary>The name the save itself gives a planet (from the planet-state records it keeps for each), or null.</summary>
        public string? NameOf(int planetHash) => PlanetNamesByHash is not null && PlanetNamesByHash.TryGetValue(planetHash, out var name) ? name : null;

        /// <summary>How many warehouse beacons (named All, Everything or Warehouse) stand on this planet.</summary>
        public int BeaconsOn(int planetHash) => BeaconPlanets?.Count(p => p == planetHash) ?? 0;

        public static StorageSnapshot Empty(string? error = null) => new(Array.Empty<StorageUnit>(), 0, null, error);
    }

    /// <summary>
    /// What the Storage tab wants the save to look like: the final item ids of every storage unit that changes, in slot order, the items thrown away, and the empty crates
    /// (by their inventory id) thrown away with them.
    /// </summary>
    public sealed record StoragePlan(IReadOnlyDictionary<long, IReadOnlyList<long>> Final, IReadOnlyCollection<long> Trashed, IReadOnlyCollection<long>? TrashedUnits = null);

    public sealed record StorageOutcome(string? NewText, int Moved, int Trashed, int UnitsDeleted, IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>
    /// Moves items between storage units in a save, and deletes items, as one edit. An item is a small record (<c>{"id":N,"gId":"Iron"}</c>) and an inventory is a
    /// record with the ids of its items in slot order (<c>"woIds"</c>) and its slot count (<c>"size"</c>), so moving an item is taking its id out of one list and putting
    /// it in another, and deleting one is also taking its own record out of the save. Nothing else is touched. The result is checked before it is returned: every other
    /// record is byte for byte what it was, each changed inventory is exactly what was asked for and within its slot count, and no item was lost or made up.
    /// </summary>
    public static class StorageTransferEngine
    {
        // What the Storage tab offers: the group ids of the things that hold items. A player's backpack is found through the player record, and a
        // truck's trunk is its first inventory (its second is its module slots, which are not storage).
        private static readonly IReadOnlyDictionary<string, string> UnitKinds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Container1"] = "Container",
            ["Container2"] = "Container",
            ["Container3"] = "Container",
            ["Locker1"] = "Locker",
            ["Fridge1"] = "Refrigerator",
            ["Vault1"] = "Vault",
            ["VehicleTruck"] = "Truck trunk",
            ["EscapePodInterplanetary"] = "Escape storage",
            ["InterplanetaryExchangePlatform1"] = "Interplanetary rocket",
            ["TradePlatform1"] = "Trade rocket",
            ["RocketTravel1"] = "Travel rocket"
        };

        public const string BackpackKind = "Backpack";

        /// <summary>A unit this close (metres, straight line) to a warehouse beacon on its planet is marked as part of the warehouse.</summary>
        public const double WarehouseRangeMeters = 200;

        // The words a warehouse beacon can be named, as everywhere else (Base Building, Travel): letters only, any case.
        private static readonly string[] WarehouseWords = { "all", "everything", "warehouse" };

        private sealed record Beacon(int Planet, double X, double Y, double Z);

        // The placed storage buildings: an empty one can be deleted (its building and its inventory go from the save, as a warehouse removal does). A backpack, a trunk, a
        // rocket's storage or the escape storage is part of something bigger and is never deleted from here.
        private static readonly HashSet<string> DeletableGIds = new(StringComparer.Ordinal) { "Container1", "Container2", "Container3", "Locker1", "Fridge1", "Vault1" };

        /// <summary>True for a storage building that can be deleted when it is empty: a container, locker, vault or refrigerator.</summary>
        public static bool IsDeletable(string gId) => DeletableGIds.Contains(gId);

        private static readonly Regex RecordPattern = new(@"\{(?:[^{}""]|""(?:[^""\\]|\\.)*"")*\}", RegexOptions.Compiled);

        private static readonly Regex IdField = new(@"^\{""id"":(-?\d+)[,}]", RegexOptions.Compiled);
        private static readonly Regex GIdField = new(@"""gId"":""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex LiIdField = new(@"""liId"":(\d+)", RegexOptions.Compiled);
        private static readonly Regex PlanetField = new(@"""planet"":(-?\d+)", RegexOptions.Compiled);
        private static readonly Regex TextField = new(@"""text"":""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
        private static readonly Regex PosField = new(@"""pos"":""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex WoIdsField = new(@"""woIds"":""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex WoIdsValue = new(@"(""woIds"":"")[^""]*("")", RegexOptions.Compiled);
        private static readonly Regex SizeField = new(@"""size"":(\d+)", RegexOptions.Compiled);
        private static readonly Regex InventoryIdField = new(@"""inventoryId"":(\d+)", RegexOptions.Compiled);
        private static readonly Regex NameField = new(@"""name"":""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
        private static readonly Regex PlanetIdField = new(@"""planetId"":""([^""]*)""", RegexOptions.Compiled);

        private sealed record Rec(int Start, int Length, string Raw, long Id, string? GId, int? LiId, int Planet, string? Text, string? Pos, string? WoIds, int? Size, int? PlayerInventory, string? PlayerName, string? PlayerPlanet)
        {
            public bool IsInventory => WoIds is not null;
        }

        /// <summary>
        /// Every storage unit in the save, backpacks first, then by kind and label, and the planet the player is registered on (the first player's own record says which;
        /// a backpack belongs to its player's planet). Fails (with the reason) only if the save looks damaged.
        /// </summary>
        public static StorageSnapshot Read(string text)
        {
            var records = Scan(text, out var problem);
            if (problem is not null)
                return StorageSnapshot.Empty(problem);

            var beacons = WarehouseBeacons(records);
            var units = Units(records, beacons, out var duplicate);
            var planet = records.FirstOrDefault(r => r.PlayerInventory is not null && !string.IsNullOrEmpty(r.PlayerPlanet))?.PlayerPlanet;
            var planetHash = planet is null ? 0 : PlayerTravelEngine.StableHash(planet);
            var names = PlayerTravelEngine.Planets(text).GroupBy(p => p.PlanetHash).ToDictionary(g => g.Key, g => g.First().PlanetId);
            return new StorageSnapshot(units, planetHash, planet, duplicate, beacons.Select(b => b.Planet).ToList(), names);
        }

        /// <summary>
        /// Applies a plan: the units named in <see cref="StoragePlan.Final"/> end up holding exactly those item ids in that order, and the trashed items are deleted from the
        /// save. Refused (nothing returned) if a unit would hold more than its slots, an item would be in two places or none, a unit is not on <paramref name="planetHash"/>
        /// (when given), or the check at the end finds anything else changed.
        /// </summary>
        public static StorageOutcome Apply(string text, StoragePlan plan, int? planetHash = null)
        {
            StorageOutcome Fail(params string[] why) => new(null, 0, 0, 0, why);

            var records = Scan(text, out var problem);
            if (problem is not null)
                return Fail(problem);

            var units = Units(records, Array.Empty<Beacon>(), out var duplicate);
            if (duplicate is not null)
                return Fail(duplicate);

            var unitByInventory = units.ToDictionary(u => u.InventoryId);

            if (planetHash is int scope && plan.Final.Keys.Any(id => unitByInventory.TryGetValue(id, out var u) && u.PlanetHash != scope))
                return Fail("A unit in the plan is not on the planet the player is on, so nothing was changed.");
            var inventories = records.Where(r => r.IsInventory).ToDictionary(r => r.Id);
            var objects = records.Where(r => !r.IsInventory && r.GId is not null).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.ToList());
            var trashed = plan.Trashed.ToList();

            // Crates to delete: a container, locker, vault or refrigerator on this planet that ends up empty, whose building record is the only one pointing at that
            // inventory. Its building and its inventory both go.
            var doomedUnits = (plan.TrashedUnits ?? Array.Empty<long>()).ToList();
            if (doomedUnits.Distinct().Count() != doomedUnits.Count)
                return Fail("A crate is listed twice for deletion, so nothing was changed.");

            var doomedContainers = new List<long>();
            foreach (var inventoryId in doomedUnits)
            {
                if (!unitByInventory.TryGetValue(inventoryId, out var crate) || !IsDeletable(crate.GId))
                    return Fail($"Unit {inventoryId} is not a container, locker, vault or refrigerator, so nothing was changed.");

                if (planetHash is int crateScope && crate.PlanetHash != crateScope)
                    return Fail($"The {crate.Kind} {inventoryId} is not on the planet the player is on, so nothing was changed.");

                var finalCount = plan.Final.TryGetValue(inventoryId, out var finalIds) ? finalIds.Count : crate.Items.Count;
                if (finalCount != 0)
                    return Fail($"The {crate.Kind} {inventoryId} is not empty, so it was not deleted and nothing was changed.");

                var owners = records.Where(r => !r.IsInventory && r.LiId == inventoryId).ToList();
                if (owners.Count != 1 || owners[0].GId != crate.GId)
                    return Fail($"The {crate.Kind} {inventoryId} does not have exactly one building record, so nothing was changed.");

                doomedContainers.Add(owners[0].Id);
            }

            var doomedUnitSet = doomedUnits.ToHashSet();
            var doomedContainerSet = doomedContainers.ToHashSet();

            // What was there, and what is asked for, must be the same set of items, with the trashed ones the only ones missing.
            var original = new Dictionary<long, long>(); // item id -> inventory it was in
            foreach (var inventoryId in plan.Final.Keys)
            {
                if (!unitByInventory.TryGetValue(inventoryId, out var unit))
                    return Fail($"Inventory {inventoryId} is not a storage unit in this save, so nothing was changed.");

                foreach (var item in unit.Items)
                {
                    if (!original.TryAdd(item.Id, inventoryId))
                        return Fail($"Item {item.Id} is in two of the units being changed; the save looks damaged, so nothing was changed.");
                }
            }

            var wanted = new Dictionary<long, long>(); // item id -> inventory it should end up in
            foreach (var (inventoryId, ids) in plan.Final)
            {
                var size = unitByInventory[inventoryId].Size;
                if (ids.Count > size)
                    return Fail($"{unitByInventory[inventoryId].Kind} {inventoryId} would hold {ids.Count} items but has only {size} slots, so nothing was changed.");

                foreach (var id in ids)
                {
                    if (!wanted.TryAdd(id, inventoryId))
                        return Fail($"Item {id} would be in two places, so nothing was changed.");
                }
            }

            if (trashed.Distinct().Count() != trashed.Count)
                return Fail("An item is listed twice for deletion, so nothing was changed.");

            foreach (var id in trashed)
            {
                if (wanted.ContainsKey(id))
                    return Fail($"Item {id} is both kept and deleted, so nothing was changed.");
            }

            var accounted = wanted.Keys.Concat(trashed).ToHashSet();
            if (!accounted.SetEquals(original.Keys))
                return Fail("The items asked for are not the items in those units (one was lost or made up), so nothing was changed.");

            // Another unit must not list an item being moved or deleted (an id in two inventories would be duplicated by the game).
            var elsewhere = units.Where(u => !plan.Final.ContainsKey(u.InventoryId)).SelectMany(u => u.Items).Select(i => i.Id).ToHashSet();
            if (accounted.Overlaps(elsewhere))
                return Fail("An item being changed is also listed in another unit; the save looks damaged, so nothing was changed.");

            foreach (var id in trashed)
            {
                if (!objects.TryGetValue(id, out var found) || found.Count != 1)
                    return Fail($"Item {id} to delete is not in the save exactly once, so nothing was changed.");

                if (found[0].LiId is not null)
                    return Fail($"Item {id} to delete holds items of its own, so nothing was changed.");
            }

            // The inventory records that change get their new item list.
            var edits = new List<(int Start, int Length, string Replacement)>();
            var replaced = new Dictionary<int, string>(); // record start -> its new text
            foreach (var (inventoryId, ids) in plan.Final)
            {
                var unit = unitByInventory[inventoryId];
                if (ids.SequenceEqual(unit.Items.Select(i => i.Id)) || doomedUnitSet.Contains(inventoryId))
                    continue; // unchanged, or a crate that is about to go (its items are all accounted for above)

                var record = inventories[inventoryId];
                if (WoIdsValue.Matches(record.Raw).Count != 1)
                    return Fail($"Inventory {inventoryId} does not have exactly one item list, so nothing was changed.");

                var newRaw = WoIdsValue.Replace(record.Raw, "${1}" + string.Join(",", ids) + "${2}");
                edits.Add((record.Start, record.Length, newRaw));
                replaced[record.Start] = newRaw;
            }

            var edited = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.Start))
                edited.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);

            var newText = edited.ToString();

            if (trashed.Count > 0 || doomedUnits.Count > 0)
            {
                var (removed, removalProblems) = BaseBuildingEngine.RemoveByIds(newText, trashed.Concat(doomedContainers).ToList(), doomedUnits);
                if (removed is null)
                    return Fail(removalProblems.ToArray());

                newText = removed;
            }

            // The check: the records that remain are the original ones, in order, with exactly the changed inventories rewritten and the trashed items gone.
            // Every record of the original, in order (those with no id of their own too: a planet's state is one), as it should now be.
            var trashedSet = trashed.ToHashSet();
            var expected = new List<string>();
            foreach (Match m in RecordPattern.Matches(text))
            {
                if (replaced.TryGetValue(m.Index, out var changed))
                {
                    expected.Add(changed);
                    continue;
                }

                // Gone: a trashed item, a deleted crate's building, and a deleted crate's inventory.
                var id = IdField.Match(m.Value);
                if (id.Success)
                {
                    var recordId = long.Parse(id.Groups[1].Value);
                    var isInventory = WoIdsField.IsMatch(m.Value);
                    if (isInventory ? doomedUnitSet.Contains(recordId) : trashedSet.Contains(recordId) || doomedContainerSet.Contains(recordId))
                        continue;
                }

                expected.Add(m.Value);
            }

            var actual = RecordPattern.Matches(newText).Select(m => m.Value).ToList();

            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                return Fail("The records after the change are not exactly the original ones with only those units rewritten, so nothing was written.");

            var eol = text.Contains("|\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var sep = "|" + eol;
            var (gapBefore, gapAfter) = (RecordPattern.Replace(text, ""), RecordPattern.Replace(newText, ""));
            var gone = gapBefore.Length - gapAfter.Length;
            if (gone < 0 || gone % sep.Length != 0 || gone / sep.Length > trashed.Count + doomedContainers.Count + doomedUnits.Count || gapBefore.Replace(sep, "") != gapAfter.Replace(sep, ""))
                return Fail("The text between the records changed by more than the separators of the deleted records, so nothing was written.");

            // And read back, each changed unit holds what was asked.
            var after = Read(newText);
            if (after.Error is not null)
                return Fail(after.Error);

            var afterByInventory = after.Units.ToDictionary(u => u.InventoryId);
            foreach (var (inventoryId, ids) in plan.Final)
            {
                if (doomedUnitSet.Contains(inventoryId))
                    continue;

                if (!afterByInventory.TryGetValue(inventoryId, out var unit) || !unit.Items.Select(i => i.Id).SequenceEqual(ids))
                    return Fail($"Reading the result back, unit {inventoryId} does not hold what was asked, so nothing was written.");
            }

            foreach (var inventoryId in doomedUnits)
            {
                if (afterByInventory.ContainsKey(inventoryId))
                    return Fail($"Reading the result back, crate {inventoryId} is still in the save, so nothing was written.");
            }

            var moved = wanted.Count(kv => original[kv.Key] != kv.Value);
            return new StorageOutcome(ReferenceEquals(newText, text) || newText == text ? null : newText, moved, trashed.Count, doomedUnits.Count, Array.Empty<string>());
        }

        // Every beacon named for the warehouse (All, Everything, Warehouse), with where it stands.
        private static List<Beacon> WarehouseBeacons(List<Rec> records)
        {
            var beacons = new List<Beacon>();

            foreach (var r in records.Where(r => !r.IsInventory && r.GId == "Beacon" && !string.IsNullOrWhiteSpace(r.Text)))
            {
                var word = new string((Unescape(r.Text) ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
                if (WarehouseWords.Contains(word) && TryPosition(r.Pos, out var x, out var y, out var z))
                    beacons.Add(new Beacon(r.Planet, x, y, z));
            }

            return beacons;
        }

        private static bool TryPosition(string? pos, out double x, out double y, out double z)
        {
            x = y = z = 0;
            var parts = pos?.Split(',');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return parts is { Length: 3 }
                && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out x)
                && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out y)
                && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out z);
        }

        private static bool NearWarehouse(Rec obj, IReadOnlyList<Beacon> beacons) =>
            TryPosition(obj.Pos, out var x, out var y, out var z)
            && beacons.Any(b => b.Planet == obj.Planet && Math.Sqrt((b.X - x) * (b.X - x) + (b.Y - y) * (b.Y - y) + (b.Z - z) * (b.Z - z)) <= WarehouseRangeMeters);

        private static List<StorageUnit> Units(List<Rec> records, IReadOnlyList<Beacon> beacons, out string? problem)
        {
            problem = null;

            var inventories = new Dictionary<long, Rec>();
            foreach (var inventory in records.Where(r => r.IsInventory))
            {
                if (!inventories.TryAdd(inventory.Id, inventory))
                {
                    problem = $"Two inventory records share the id {inventory.Id}; the save looks damaged.";
                    return new List<StorageUnit>();
                }
            }

            var objects = new Dictionary<long, Rec>();
            foreach (var obj in records.Where(r => !r.IsInventory && r.GId is not null))
                objects.TryAdd(obj.Id, obj);

            List<StoredItem> ItemsOf(Rec inventory) =>
                (inventory.WoIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => long.TryParse(s, out var id) ? id : 0)
                    .Where(id => id != 0)
                    .Select(id => new StoredItem(id, objects.TryGetValue(id, out var o) ? o.GId! : ""))
                    .ToList();

            var units = new List<StorageUnit>();
            var seenInventories = new HashSet<long>();

            foreach (var player in records.Where(r => r.PlayerInventory is not null))
            {
                if (inventories.TryGetValue(player.PlayerInventory!.Value, out var pack) && pack.Size is int size && seenInventories.Add(pack.Id))
                    units.Add(new StorageUnit(pack.Id, BackpackKind, "", Unescape(player.PlayerName) is { Length: > 0 } name ? name : "Player", "",
                        string.IsNullOrEmpty(player.PlayerPlanet) ? 0 : PlayerTravelEngine.StableHash(player.PlayerPlanet), size, ItemsOf(pack)));
            }

            foreach (var obj in records.Where(r => !r.IsInventory && r.GId is not null && r.LiId is not null && UnitKinds.ContainsKey(r.GId)))
            {
                if (inventories.TryGetValue(obj.LiId!.Value, out var inventory) && inventory.Size is int size && seenInventories.Add(inventory.Id))
                    units.Add(new StorageUnit(inventory.Id, UnitKinds[obj.GId!], obj.GId!, Unescape(obj.Text) ?? "", Short(obj.Pos), obj.Planet, size, ItemsOf(inventory), IsDeletable(obj.GId!) && NearWarehouse(obj, beacons)));
            }

            return units
                .OrderBy(u => u.Kind == BackpackKind ? 0 : 1)
                .ThenBy(u => u.Kind, StringComparer.Ordinal)
                .ThenBy(u => u.GId, StringComparer.Ordinal)
                .ThenBy(u => u.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.InventoryId)
                .ToList();
        }

        private static List<Rec> Scan(string text, out string? problem)
        {
            problem = null;
            var records = new List<Rec>();

            foreach (Match m in RecordPattern.Matches(text))
            {
                var raw = m.Value;
                var id = IdField.Match(raw);
                if (!id.Success)
                    continue; // a record with no id of its own (a planet's state): nothing here looks at those

                var gid = GIdField.Match(raw);

                // Most of a save is plain items ({"id":N,"gId":"Iron"}): nothing here needs more than their id and group, so they are not picked apart further.
                // Only what has an inventory, is an inventory, is a player, or is a beacon (the warehouse's) needs its other fields.
                if (!(raw.Contains("\"liId\"", StringComparison.Ordinal) || raw.Contains("\"woIds\"", StringComparison.Ordinal)
                    || raw.Contains("\"inventoryId\"", StringComparison.Ordinal) || (gid.Success && gid.Groups[1].Value == "Beacon")))
                {
                    records.Add(new Rec(m.Index, m.Length, raw, long.Parse(id.Groups[1].Value), gid.Success ? gid.Groups[1].Value : null, null, 0, null, null, null, null, null, null, null));
                    continue;
                }

                var woIds = WoIdsField.Match(raw);
                var size = SizeField.Match(raw);
                var li = LiIdField.Match(raw);
                var planet = PlanetField.Match(raw);
                var inventoryId = InventoryIdField.Match(raw);

                records.Add(new Rec(
                    m.Index, m.Length, raw, long.Parse(id.Groups[1].Value),
                    gid.Success ? gid.Groups[1].Value : null,
                    li.Success ? int.Parse(li.Groups[1].Value) : null,
                    planet.Success ? int.Parse(planet.Groups[1].Value) : 0,
                    TextField.Match(raw) is { Success: true } t ? t.Groups[1].Value : null,
                    PosField.Match(raw) is { Success: true } p ? p.Groups[1].Value : null,
                    woIds.Success ? woIds.Groups[1].Value : null,
                    woIds.Success && size.Success ? int.Parse(size.Groups[1].Value) : null,
                    inventoryId.Success && !woIds.Success ? int.Parse(inventoryId.Groups[1].Value) : null,
                    inventoryId.Success && !woIds.Success && NameField.Match(raw) is { Success: true } n ? n.Groups[1].Value : null,
                    inventoryId.Success && !woIds.Success && PlanetIdField.Match(raw) is { Success: true } pl ? pl.Groups[1].Value : null));
            }

            if (records.Count == 0)
                problem = "No records were found in the save, so it was not read.";

            return records;
        }

        private static string? Unescape(string? json)
        {
            if (json is null)
                return null;

            try
            {
                return JsonSerializer.Deserialize<string>("\"" + json + "\"");
            }
            catch (JsonException)
            {
                return json;
            }
        }

        // "x,y,z" as "x, z" in whole metres, which is how everything else here names a place.
        private static string Short(string? pos)
        {
            var parts = pos?.Split(',');
            return parts is { Length: 3 }
                && double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var x)
                && double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var z)
                ? $"{Math.Round(x)}, {Math.Round(z)}"
                : "";
        }
    }
}
