using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>What happened for one config. Containers is 0 when nothing carries the label.</summary>
    public sealed record ResupplyLine(ResupplyConfig Config, int ContainersFound, int ItemsRelabeled, int ItemsAdded)
    {
        public bool Changed => ItemsRelabeled > 0 || ItemsAdded > 0;
    }

    public sealed record ResupplyOutcome(string? NewText, IReadOnlyList<ResupplyLine> Lines, IReadOnlyList<string> Problems)
    {
        public bool Changed => NewText != null;
        public bool Failed => Problems.Count > 0;
    }

    /// <summary>
    /// Empties and refills labelled storage containers in the text of a Planet Crafter save, driven by a list of
    /// user-configured (container label, product) pairs from the Cheats page.
    ///
    /// Generalized from RRSOS-PCC's SaveResupplier, which only ever topped up free slots for three hardcoded
    /// labels. This one first "empties" a container by rewriting every item it already holds to the target
    /// product's gId, in place: no record is deleted and no id is reallocated, so a slot that held a wrench now
    /// holds a wrench-shaped record that says it is the product instead. Only genuinely free slots get brand-new
    /// records, exactly as the old engine did. The end state is the same as a literal empty-then-fill (the
    /// container holds only the product, up to its slot count) without the extra risk of deleting text out of the
    /// save.
    ///
    /// Same proof obligation as before: undoing every edit must give back the original text exactly. Any problem
    /// means no new text is returned at all. Pure text-in, text-out, so it is testable without a game or a file.
    /// </summary>
    public static class SaveResupplyEngine
    {
        // One flat JSON object, aware of strings so braces inside text values cannot end it early.
        // Records in a save never nest objects.
        private static readonly Regex RecordPattern = new(
            @"\{(?:[^{}""]|""(?:[^""\\]|\\.)*"")*\}",
            RegexOptions.Compiled);

        private static readonly Regex WoIdsValue = new(@"(""woIds"":"")[^""]*("")", RegexOptions.Compiled);
        private static readonly Regex GIdValue = new(@"(""gId"":"")[^""]*("")", RegexOptions.Compiled);

        // The range the game hands out ids from for crafted items.
        private const int NewIdMin = 200_000_000;
        private const int NewIdMax = 210_000_000;

        private sealed record Rec(int Start, int Length, string Raw, long Id, string? GId, string? Text, int? LiId, string? WoIds, int? Size)
        {
            public bool IsInventory => WoIds != null;
        }

        private readonly record struct Edit(int Start, int Length, string Replacement);

        /// <summary>An existing item whose own gId was rewritten in place.</summary>
        private sealed record ItemExpectation(long Id, string OldRaw, string NewRaw);

        /// <summary>What one topped-up container's inventory record should look like afterwards.</summary>
        private sealed record InventoryExpectation(long InventoryId, List<long> NewIds, string InsertedText, string OldRaw, string NewRaw);

        /// <summary>An inventory that had at least one edit applied, and what every item in it should be afterwards.</summary>
        private sealed record TouchedInventory(long InventoryId, string GId);

        public static ResupplyOutcome Apply(string text, IReadOnlyList<ResupplyConfig> configs, Random? random = null)
        {
            random ??= Random.Shared;

            var records = Scan(text, out var problems);
            if (problems.Count > 0)
                return Failed(problems);

            var inventories = new Dictionary<long, Rec>();
            foreach (var inventory in records.Where(r => r.IsInventory))
            {
                if (!inventories.TryAdd(inventory.Id, inventory))
                    return Failed($"Two inventory records share the id {inventory.Id}; the save looks damaged.");
            }

            var objects = new Dictionary<long, Rec>();
            foreach (var obj in records.Where(r => !r.IsInventory && r.GId != null))
            {
                if (!objects.TryAdd(obj.Id, obj))
                    return Failed($"Two object records share the id {obj.Id}; the save looks damaged.");
            }

            var used = new HashSet<long>(records.Select(r => r.Id));
            var eol = text.Contains("|\r\n") ? "\r\n" : "\n";

            var edits = new List<Edit>();
            var lines = new List<ResupplyLine>();
            var handledInventories = new HashSet<long>();
            var itemExpectations = new List<ItemExpectation>();
            var inventoryExpectations = new List<InventoryExpectation>();
            var touched = new List<TouchedInventory>();

            foreach (var config in configs)
            {
                var label = config.ContainerLabel?.Trim() ?? "";

                if (label.Length == 0 || string.IsNullOrWhiteSpace(config.ProductGId))
                {
                    lines.Add(new ResupplyLine(config, 0, 0, 0));
                    continue;
                }

                var containers = records
                    .Where(r => !r.IsInventory && r.LiId is not null
                                && string.Equals(r.Text?.Trim(), label, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var relabeled = 0;
                var added = 0;

                foreach (var container in containers)
                {
                    var liId = container.LiId!.Value;

                    if (!inventories.TryGetValue(liId, out var inventory))
                        return Failed($"Container \"{label}\" points at inventory {liId}, which is not in the save.");

                    if (!handledInventories.Add(liId))
                        continue; // this inventory was already handled under another config's container

                    var existing = SplitIds(inventory.WoIds!);
                    var free = (inventory.Size ?? 0) - existing.Count;
                    var changedHere = false;

                    foreach (var id in existing)
                    {
                        if (!objects.TryGetValue(id, out var item))
                            return Failed($"Inventory {liId} lists item {id}, which is not in the save.");

                        if (string.Equals(item.GId, config.ProductGId, StringComparison.Ordinal))
                            continue; // already the target product

                        var updated = GIdValue.Replace(item.Raw, m => m.Groups[1].Value + config.ProductGId + m.Groups[2].Value, 1);

                        edits.Add(new Edit(item.Start, item.Length, updated));
                        itemExpectations.Add(new ItemExpectation(id, item.Raw, updated));
                        relabeled++;
                        changedHere = true;
                    }

                    if (free > 0)
                    {
                        var newIds = NewIds(free, used, random);

                        // The new records go in front of the container's own record, which always starts on a
                        // record boundary, and each one ends the way every other record does.
                        var items = string.Concat(newIds.Select(id => "{\"id\":" + id + ",\"gId\":\"" + config.ProductGId + "\"}|" + eol));
                        var updatedInventory = WoIdsValue.Replace(inventory.Raw, m => m.Groups[1].Value + string.Join(",", existing.Concat(newIds)) + m.Groups[2].Value, 1);

                        edits.Add(new Edit(container.Start, 0, items));
                        edits.Add(new Edit(inventory.Start, inventory.Length, updatedInventory));
                        inventoryExpectations.Add(new InventoryExpectation(liId, newIds, items, inventory.Raw, updatedInventory));
                        added += newIds.Count;
                        changedHere = true;
                    }

                    if (changedHere)
                        touched.Add(new TouchedInventory(liId, config.ProductGId));
                }

                lines.Add(new ResupplyLine(config, containers.Count, relabeled, added));
            }

            if (edits.Count == 0)
                return new ResupplyOutcome(null, lines, Array.Empty<string>());

            // Apply from the end of the text backwards so earlier positions stay valid.
            var result = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.Start))
            {
                result.Remove(edit.Start, edit.Length);
                result.Insert(edit.Start, edit.Replacement);
            }

            var newText = result.ToString();
            var check = Verify(text, newText, itemExpectations, inventoryExpectations, touched);

            return check.Count > 0 ? new ResupplyOutcome(null, lines, check) : new ResupplyOutcome(newText, lines, Array.Empty<string>());
        }

        // ------------------------------------------------------------------

        private static ResupplyOutcome Failed(string problem) => new(null, Array.Empty<ResupplyLine>(), new[] { problem });

        private static ResupplyOutcome Failed(List<string> problems) => new(null, Array.Empty<ResupplyLine>(), problems);

        private static List<long> SplitIds(string woIds) =>
            woIds.Length == 0 ? new() : woIds.Split(',').Select(long.Parse).ToList();

        private static List<long> NewIds(int count, HashSet<long> used, Random random)
        {
            var ids = new List<long>(count);
            while (ids.Count < count)
            {
                var candidate = random.NextInt64(NewIdMin, NewIdMax);
                if (used.Add(candidate))
                    ids.Add(candidate);
            }

            return ids;
        }

        /// <summary>Finds and parses every record. Anything that is not valid JSON is a problem.</summary>
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
                        continue; // a record without an id (settings, messages, ...) is none of our business

                    string? Str(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    int? Int(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : null;

                    records.Add(new Rec(match.Index, match.Length, match.Value, id, Str("gId"), Str("text"), Int("liId"), Str("woIds"), Int("size")));
                }
                catch (JsonException ex)
                {
                    problems.Add($"A record at position {match.Index} is not valid JSON: {ex.Message}");
                }
            }

            return records;
        }

        /// <summary>Checks the edited text against the original before anyone is allowed to write it.</summary>
        private static List<string> Verify(
            string before, string after,
            List<ItemExpectation> itemExpectations,
            List<InventoryExpectation> inventoryExpectations,
            List<TouchedInventory> touched)
        {
            var problems = new List<string>();

            var records = Scan(after, out var scanProblems);
            problems.AddRange(scanProblems);
            if (problems.Count > 0)
                return problems;

            // 1. The strong check: undo every edit (item relabels first, then the inventory top-ups) and the
            //    original must come back byte for byte.
            var restored = after;

            foreach (var e in itemExpectations)
            {
                var at = restored.IndexOf(e.NewRaw, StringComparison.Ordinal);
                if (at < 0)
                {
                    problems.Add($"Relabeled item {e.Id} was not found in the result.");
                    continue;
                }

                restored = restored.Remove(at, e.NewRaw.Length).Insert(at, e.OldRaw);
            }

            foreach (var e in inventoryExpectations)
            {
                var at = restored.IndexOf(e.InsertedText, StringComparison.Ordinal);
                if (at < 0)
                {
                    problems.Add($"Inserted items for inventory {e.InventoryId} were not found in the result.");
                    continue;
                }

                restored = restored.Remove(at, e.InsertedText.Length);

                var raw = restored.IndexOf(e.NewRaw, StringComparison.Ordinal);
                if (raw < 0)
                {
                    problems.Add($"Updated inventory {e.InventoryId} was not found in the result.");
                    continue;
                }

                restored = restored.Remove(raw, e.NewRaw.Length).Insert(raw, e.OldRaw);
            }

            if (problems.Count == 0 && !string.Equals(restored, before, StringComparison.Ordinal))
                problems.Add("Undoing the edit does not give back the original save, so something else changed. Nothing was written.");

            // 2. The structural checks: what the game will actually read.
            var objects = new Dictionary<long, Rec>();
            foreach (var obj in records.Where(r => !r.IsInventory && r.GId != null))
            {
                if (!objects.TryAdd(obj.Id, obj))
                    problems.Add($"Object id {obj.Id} appears twice after the edit.");
            }

            // Objects and inventories can share an id number by coincidence, so each kind is checked on its own.
            var inventories = records.Where(r => r.IsInventory).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var t in touched)
            {
                if (!inventories.TryGetValue(t.InventoryId, out var found) || found.Count != 1)
                {
                    problems.Add($"Inventory {t.InventoryId} is missing or duplicated after the edit.");
                    continue;
                }

                var held = SplitIds(found[0].WoIds!);

                if (held.Count > (found[0].Size ?? 0))
                    problems.Add($"Inventory {t.InventoryId} would hold {held.Count} items in {found[0].Size} slots.");

                foreach (var id in held)
                {
                    if (!objects.TryGetValue(id, out var item) || item.GId != t.GId)
                        problems.Add($"Item {id} in inventory {t.InventoryId} is missing or is not a {t.GId}.");
                }
            }

            return problems;
        }
    }
}
