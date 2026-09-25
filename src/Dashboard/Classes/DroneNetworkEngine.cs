using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// What a container does for the drones. Demand: it asks for its product to be brought (a sink, where a producer's
    /// output ends up). Supply: it offers its product to whatever demands it (a source, like a chest beside a crafter).
    /// Never both: a demand container cannot feed anything else, so it is no warehouse.
    /// </summary>
    public enum ContainerMode { Demand, Supply }

    /// <summary>
    /// What to do with one container or producer, by inventory id. On sets it up, off clears it. Item null keeps the
    /// default (a container's product; a producer's own contents); Mode is for containers only.
    /// </summary>
    public sealed record DroneWish(bool On, string? Item = null, ContainerMode Mode = ContainerMode.Demand);

    public enum DroneSupplyState { None, Partial, All }

    /// <summary>One producer, as the Drone Network list shows it. <paramref name="InventoryId"/> is what a change is asked for by.</summary>
    /// <param name="Position">Where it stands, "x, z" in whole metres, or empty.</param>
    /// <param name="Contents">What it holds right now, like "Iron x4, Cobalt x2", or empty.</param>
    /// <param name="Products">The kinds of item it holds now, comma-separated: what it will supply. Empty when it holds nothing.</param>
    /// <param name="Supplies">What its supply list says now, comma-separated.</param>
    /// <param name="State">Supplies nothing, supplies its products, or supplies something else.</param>
    public sealed record DroneProducer(long InventoryId, string GId, string Label, string Position, string Contents, string Products, string Supplies, DroneSupplyState State);

    /// <summary>Where a consumer stands. Machine: a machine that demands things by its own settings, shown but not changed.</summary>
    public enum DroneDemandState
    {
        /// <summary>A container with a product that neither demands nor supplies yet.</summary>
        NotSet,
        /// <summary>Demands its product and supplies nothing, or supplies its product and demands nothing.</summary>
        Set,
        /// <summary>Demands its product but also supplies things (an old "supply everything else" setup): switching it makes it a plain sink.</summary>
        Partial,
        /// <summary>Anything else: it has settings of its own that do not fit.</summary>
        Other,
        /// <summary>A mixed container ("Misc", or no label with a bunch of different items): no product, ignored unless the user picks an item.</summary>
        Ignored,
        /// <summary>A machine with demands of its own. Shown, never changed.</summary>
        Machine
    }

    /// <summary>One consumer, as the Drone Network list shows it: a storage container, or a machine that demands things.</summary>
    /// <param name="InventoryId">What a change is asked for by (containers only).</param>
    /// <param name="GId">The object's type: Container1, AnimalFeeder1, ...</param>
    /// <param name="Label">The container's label.</param>
    /// <param name="Item">The container's product (its label's item, else the one thing it holds); empty when it has none.</param>
    /// <param name="Mode">Demand or supply when it is set up that way, else null.</param>
    public sealed record DroneConsumer(
        long InventoryId, string GId, string Label, string Item, string Position, string Demands, string Supplies,
        DroneDemandState State, ContainerMode? Mode)
    {
        public bool Editable => State != DroneDemandState.Machine;
    }

    /// <summary>What a Set Supply Lines run did (or, for a preview, would do) to one save's text.</summary>
    /// <param name="Stations">Drone stations in the save. Nothing is edited without at least one.</param>
    /// <param name="ContainersActivated">Containers set up as asked, and that were changed.</param>
    /// <param name="ContainersCleared">Containers asked to stop, and that were changed.</param>
    /// <param name="ProducersActivated">Producers set to supply, and that were changed.</param>
    /// <param name="ProducersCleared">Producers asked to stop supplying, and that were changed.</param>
    /// <param name="Producers">Every producer in the save with what it does now.</param>
    /// <param name="Consumers">Every consumer in the save with what it does now.</param>
    public sealed record DroneNetworkOutcome(
        string? NewText,
        int Stations,
        int ContainersActivated,
        int ContainersCleared,
        int ProducersActivated,
        int ProducersCleared,
        IReadOnlyList<DroneProducer> Producers,
        IReadOnlyList<DroneConsumer> Consumers,
        IReadOnlyList<string> Problems)
    {
        public bool Failed => Problems.Count > 0;
        public bool Changed => NewText != null;
    }

    /// <summary>
    /// The Cheats page's Drone Network: in the text of a Planet Crafter save, sets the drone logistics of chosen producers
    /// (extractors, collectors, beehives, ...) and chosen storage containers (Container1, 2 and 3). The game keeps these
    /// settings on the inventory a container or machine points at: <c>demandGrps</c>, <c>supplyGrps</c> (comma-separated
    /// group ids, which for items are their gIds) and <c>priority</c>. Drones carry an item from something that supplies
    /// it to something that demands it.
    ///
    /// The owner's model, and all this writes: a producer supplies what it makes (the kinds of item it holds); a container
    /// is a sink (demands its one product) or a source (supplies its one product), never both. Short lists, however many
    /// items the game knows. A container's product is what its label names (a game id, or an item's name like "Mushroom"),
    /// else the one thing it holds when everything in it is alike; a mixed container has none and is ignored, unless the
    /// user picks an item for it.
    ///
    /// Nothing is changed unless asked for by inventory id. Only those three fields of the chosen inventory records are
    /// rewritten; no record is added or removed. Same proof obligation as <see cref="SaveResupplyEngine"/>: undoing every
    /// edit gives back the original text exactly, or nothing is returned. Pure text-in, text-out.
    /// </summary>
    public static partial class DroneNetworkEngine
    {
        /// <summary>
        /// The item value that means "everything": for a source container only, like the chest at the base entrance where
        /// the owner empties their pockets and that supplies every item to whatever demands it.
        /// </summary>
        public const string Everything = "*";

        private static readonly Regex RecordPattern = new(
            @"\{(?:[^{}""]|""(?:[^""\\]|\\.)*"")*\}",
            RegexOptions.Compiled);

        private static readonly Regex StorageContainer = new(@"^Container[123]$", RegexOptions.Compiled);

        // Things that make a product. Farms, growers and spreaders are not here: they take their inputs and hold their outputs,
        // which the owner does not send out. Autocrafters are not either: they hold ingredients as well as what they make, so
        // what they hold says nothing about what they supply.
        private static readonly Regex Producer = new(
            @"^(OreExtractor|GasExtractor|WaterCollector|WaterLifeCollector|Beehive|Biodome|Ecosystem|HarvestingRobot|SilkGenerator)\d*$",
            RegexOptions.Compiled);

        // Extractors pull random ore or gas, and ecosystems produce random larvae, so what they hold says nothing about what they
        // will hold next: they supply everything.
        private static readonly Regex Extractor = new(@"^(OreExtractor|GasExtractor|Ecosystem)\d*$", RegexOptions.Compiled);

        private static readonly Regex DemandValue = new(@"""demandGrps"":""[^""]*""", RegexOptions.Compiled);
        private static readonly Regex SupplyValue = new(@"""supplyGrps"":""[^""]*""", RegexOptions.Compiled);
        private static readonly Regex PriorityValue = new(@"""priority"":-?\d+", RegexOptions.Compiled);

        private sealed record Rec(int Start, int Length, string Raw, long Id, string? GId, string? Text, int? LiId, bool IsInventory,
            string? Demand, string? Supply, int? Priority, string? Pos, string? WoIdsText);

        private readonly record struct Edit(int Start, int Length, string Old, string New);

        /// <param name="resolveItem">The item a label names (a game id or an item's name), or null when the label names none.</param>
        /// <param name="producerWishes">Producers to change, by inventory id.</param>
        /// <param name="containerWishes">Containers to change, by inventory id.</param>
        public static DroneNetworkOutcome Apply(
            string text, Func<string, string?> resolveItem,
            IReadOnlyDictionary<long, DroneWish>? producerWishes = null, IReadOnlyDictionary<long, DroneWish>? containerWishes = null)
        {
            var records = Scan(text, out var problems);
            if (problems.Count > 0)
                return Failed(problems);

            var inventories = new Dictionary<long, Rec>();
            foreach (var inventory in records.Where(r => r.IsInventory))
            {
                if (!inventories.TryAdd(inventory.Id, inventory))
                    return Failed($"Two inventory records share the id {inventory.Id}; the save looks damaged.");
            }

            var objects = records.Where(r => !r.IsInventory && r.GId != null).ToList();
            var stations = objects.Count(o => o.GId!.StartsWith("DroneStation", StringComparison.Ordinal));

            var byId = new Dictionary<long, Rec>();
            foreach (var o in objects)
                byId.TryAdd(o.Id, o);

            var edits = new List<Edit>();
            var handled = new HashSet<long>();
            var containersActivated = 0;
            var containersCleared = 0;
            var producersActivated = 0;
            var producersCleared = 0;
            var producers = new List<DroneProducer>();
            var consumers = new List<DroneConsumer>();

            foreach (var obj in objects)
            {
                if (obj.LiId is null)
                    continue;

                var isContainer = StorageContainer.IsMatch(obj.GId!);
                var isProducer = !isContainer && Producer.IsMatch(obj.GId!);

                // Anything else only matters here if it has demands of its own (an animal feeder, the trade platform).
                if (!isContainer && !isProducer)
                {
                    if (inventories.TryGetValue(obj.LiId.Value, out var machine) && Split(machine.Demand).Count > 0 && handled.Add(machine.Id))
                        consumers.Add(new DroneConsumer(machine.Id, obj.GId!, obj.Text?.Trim() ?? "", "", Where(obj.Pos), machine.Demand!, machine.Supply ?? "", DroneDemandState.Machine, null));

                    continue;
                }

                var liId = obj.LiId.Value;
                if (!inventories.TryGetValue(liId, out var inv))
                    return Failed($"\"{obj.GId}\" {obj.Id} points at inventory {liId}, which is not in the save.");

                if (!handled.Add(liId))
                    continue; // one inventory, one setting

                var priority = inv.Priority ?? 0;
                var demand = Split(inv.Demand);
                var supply = Split(inv.Supply);

                if (isProducer)
                {
                    // What it should supply: everything for an extractor, else the kinds of item it holds.
                    var isExtractor = Extractor.IsMatch(obj.GId!);
                    var products = isExtractor ? LearnEverything(inventories.Values) : HeldKinds(inv, byId);
                    var state = supply.Count == 0 ? DroneSupplyState.None
                        : products.Count > 0 && supply.Count == products.Count && !products.Except(supply, StringComparer.Ordinal).Any() ? DroneSupplyState.All
                        : DroneSupplyState.Partial;

                    producers.Add(new DroneProducer(liId, obj.GId!, obj.Text?.Trim() ?? "", Where(obj.Pos), Held(inv, byId), isExtractor ? Everything : string.Join(",", products), inv.Supply ?? "", state));

                    if (producerWishes is null || !producerWishes.TryGetValue(liId, out var wish))
                        continue;

                    // On: supply the chosen item, else what it holds. Nothing to supply: nothing to do. Its own demand stays.
                    var supplies = wish.Item?.Trim() == Everything ? string.Join(",", LearnEverything(inventories.Values))
                        : !string.IsNullOrWhiteSpace(wish.Item) ? wish.Item.Trim()
                        : string.Join(",", products);
                    if (wish.On && supplies.Length == 0)
                        continue;

                    var changedProducer = Rewrite(inv.Raw, string.Join(",", demand), wish.On ? supplies : "", priority);
                    if (string.Equals(changedProducer, inv.Raw, StringComparison.Ordinal))
                        continue;

                    edits.Add(new Edit(inv.Start, inv.Length, inv.Raw, changedProducer));
                    if (wish.On) producersActivated++; else producersCleared++;
                    continue;
                }

                // A container's product: what its label names, else the one thing it holds when all of it is alike.
                var label = obj.Text?.Trim() ?? "";
                var product = label.Length == 0 ? null : resolveItem(label);
                product ??= SoleProduct(inv, byId);

                var demandsIt = product is not null && demand.Count == 1 && demand[0] == product;
                var suppliesIt = product is not null && supply.Count == 1 && supply[0] == product;

                var demandState = product is null ? (demand.Count == 0 && supply.Count == 0 ? DroneDemandState.Ignored : DroneDemandState.Other)
                    : demand.Count == 0 && supply.Count == 0 ? DroneDemandState.NotSet
                    : demandsIt && supply.Count == 0 || suppliesIt && demand.Count == 0 ? DroneDemandState.Set
                    : demandsIt ? DroneDemandState.Partial
                    : DroneDemandState.Other;

                ContainerMode? mode = demandState == DroneDemandState.Set ? (demandsIt ? ContainerMode.Demand : ContainerMode.Supply)
                    : demandState == DroneDemandState.Partial ? ContainerMode.Demand
                    : null;

                consumers.Add(new DroneConsumer(liId, obj.GId!, label, product ?? "", Where(obj.Pos), inv.Demand ?? "", inv.Supply ?? "", demandState, mode));

                if (containerWishes is null || !containerWishes.TryGetValue(liId, out var want))
                    continue;

                // On: demand the item (a sink) or supply it (a source), never both. Nothing to move: nothing to do.
                var chosen = !string.IsNullOrWhiteSpace(want.Item) ? want.Item.Trim() : product;
                if (want.On && chosen is null)
                    continue;

                // "Everything" only makes sense as a source; a sink demanding everything is refused.
                if (want.On && chosen == Everything && want.Mode == ContainerMode.Demand)
                    continue;

                var supplied = want.On && chosen == Everything ? string.Join(",", LearnEverything(inventories.Values)) : chosen;

                var changed = !want.On ? Rewrite(inv.Raw, "", "", priority)
                    : want.Mode == ContainerMode.Demand ? Rewrite(inv.Raw, chosen!, "", priority)
                    : Rewrite(inv.Raw, "", supplied!, priority);

                if (string.Equals(changed, inv.Raw, StringComparison.Ordinal))
                    continue;

                edits.Add(new Edit(inv.Start, inv.Length, inv.Raw, changed));
                if (want.On) containersActivated++; else containersCleared++;
            }

            DroneNetworkOutcome Outcome(string? newText, IReadOnlyList<string> p) => new(
                newText, stations, containersActivated, containersCleared, producersActivated, producersCleared, producers, consumers, p);

            // Without a drone station there is no network to set up: report what is there, change nothing.
            if (stations == 0 || edits.Count == 0)
                return Outcome(null, Array.Empty<string>());

            var result = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.Start))
            {
                result.Remove(edit.Start, edit.Length);
                result.Insert(edit.Start, edit.New);
            }

            var newText = result.ToString();
            var check = Verify(text, newText, edits);

            return check.Count > 0 ? Outcome(null, check) : Outcome(newText, Array.Empty<string>());
        }

        // ------------------------------------------------------------------

        private static DroneNetworkOutcome Failed(string problem) => Failed(new List<string> { problem });

        private static DroneNetworkOutcome Failed(List<string> problems) =>
            new(null, 0, 0, 0, 0, 0, Array.Empty<DroneProducer>(), Array.Empty<DroneConsumer>(), problems);

        /// <summary>
        /// The set of groups "supply everything" means for this save: the longest supply list it already has, then anything
        /// any other list (or any demand) adds, in the order first seen. A save with no drone settings gets the built-in list.
        /// </summary>
        private static List<string> LearnEverything(IEnumerable<Rec> inventories)
        {
            var all = inventories.ToList();
            var lists = all.Select(i => Split(i.Supply)).Where(l => l.Count > 20).OrderByDescending(l => l.Count).ToList();
            if (lists.Count == 0)
                return FallbackEverything.ToList();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var everything = new List<string>();

            foreach (var group in lists.SelectMany(l => l).Concat(all.SelectMany(i => Split(i.Demand))))
            {
                if (seen.Add(group))
                    everything.Add(group);
            }

            return everything;
        }

        /// <summary>The kinds of item an inventory holds, in the order first seen.</summary>
        private static List<string> HeldKinds(Rec inventory, Dictionary<long, Rec> objects) =>
            Split(inventory.WoIdsText)
                .Select(id => long.TryParse(id, out var n) && objects.TryGetValue(n, out var o) ? o.GId : null)
                .Where(g => g != null)
                .Select(g => g!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        /// <summary>The one item an inventory is full of, or null when it is empty or holds more than one kind.</summary>
        private static string? SoleProduct(Rec inventory, Dictionary<long, Rec> objects)
        {
            var kinds = HeldKinds(inventory, objects);
            return kinds.Count == 1 ? kinds[0] : null;
        }

        private static string Where(string? pos)
        {
            var parts = pos?.Split(',');
            return parts is { Length: 3 } && double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var x)
                && double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var z)
                ? $"{Math.Round(x)}, {Math.Round(z)}"
                : "";
        }

        /// <summary>What an inventory holds, as "Iron x4, Cobalt x2" (the three most common; "..." when there are more kinds).</summary>
        private static string Held(Rec inventory, Dictionary<long, Rec> objects)
        {
            var kinds = Split(inventory.WoIdsText)
                .Select(id => long.TryParse(id, out var n) && objects.TryGetValue(n, out var o) ? o.GId : null)
                .Where(g => g != null)
                .GroupBy(g => g!)
                .OrderByDescending(g => g.Count())
                .ToList();

            return string.Join(", ", kinds.Take(3).Select(g => $"{g.Key} x{g.Count()}")) + (kinds.Count > 3 ? ", ..." : "");
        }

        private static List<string> Split(string? list) =>
            string.IsNullOrEmpty(list) ? new() : list.Split(',').ToList();

        /// <summary>Sets the three fields in an inventory record's text, adding any it does not have yet, at its end.</summary>
        private static string Rewrite(string raw, string demand, string supply, int priority)
        {
            var demandField = "\"demandGrps\":\"" + demand + "\"";
            var supplyField = "\"supplyGrps\":\"" + supply + "\"";
            var priorityField = "\"priority\":" + priority;

            var text = raw;
            var missing = new StringBuilder();

            foreach (var (pattern, field) in new[] { (DemandValue, demandField), (SupplyValue, supplyField), (PriorityValue, priorityField) })
            {
                if (pattern.IsMatch(text))
                    text = pattern.Replace(text, _ => field, 1);
                else
                    missing.Append(',').Append(field);
            }

            // Every record ends with its closing brace; new fields go just before it.
            return missing.Length == 0 ? text : text[..^1] + missing + "}";
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

                    records.Add(new Rec(match.Index, match.Length, match.Value, id, Str("gId"), Str("text"), Int("liId"),
                        root.TryGetProperty("woIds", out _), Str("demandGrps"), Str("supplyGrps"), Int("priority"), Str("pos"), Str("woIds")));
                }
                catch (JsonException ex)
                {
                    problems.Add($"A record at position {match.Index} is not valid JSON: {ex.Message}");
                }
            }

            return records;
        }

        /// <summary>Checks the edited text against the original before anyone is allowed to write it.</summary>
        private static List<string> Verify(string before, string after, List<Edit> edits)
        {
            var records = Scan(after, out var problems);
            if (problems.Count > 0)
                return problems;

            // Undo every edit and the original must come back byte for byte.
            var restored = after;
            foreach (var e in edits)
            {
                var at = restored.IndexOf(e.New, StringComparison.Ordinal);
                if (at < 0)
                {
                    problems.Add("An edited record was not found in the result.");
                    continue;
                }

                restored = restored.Remove(at, e.New.Length).Insert(at, e.Old);
            }

            if (problems.Count == 0 && !string.Equals(restored, before, StringComparison.Ordinal))
                problems.Add("Undoing the edit does not give back the original save, so something else changed. Nothing was written.");

            // What the game will read: each edited inventory still has its id, its items and its size, and the new lists.
            var byId = records.Where(r => r.IsInventory).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.ToList());
            var originals = Scan(before, out _).Where(r => r.IsInventory).ToDictionary(r => r.Id);

            foreach (var e in edits)
            {
                var id = Scan(e.Old, out _).FirstOrDefault()?.Id;
                if (id is null || !byId.TryGetValue(id.Value, out var found) || found.Count != 1)
                {
                    problems.Add($"Inventory {id} is missing or duplicated after the edit.");
                    continue;
                }

                using var now = JsonDocument.Parse(found[0].Raw);
                using var was = JsonDocument.Parse(originals[id.Value].Raw);

                foreach (var key in new[] { "woIds", "size" })
                {
                    if (now.RootElement.GetProperty(key).ToString() != was.RootElement.GetProperty(key).ToString())
                        problems.Add($"Inventory {id}: {key} changed.");
                }

            }

            return problems;
        }
    }
}
