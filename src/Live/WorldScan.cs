using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// One pass over the game's placed objects, collecting the slow-moving facts the dashboard needs to work out
    /// its bases and extractors: pods (with their panels), signs, what the containers hold, loose items on the
    /// ground, and the extractors. It reports raw facts. Deciding what a "base" is, naming it and grouping
    /// things by base all happen in the dashboard (see docs/contract.md, the world file).
    ///
    /// The game can hold tens of thousands of objects, so the pass is spread over frames: it works for about
    /// <see cref="FrameBudgetMs"/> milliseconds and then gives the game its frame back.
    /// </summary>
    internal sealed class WorldScan
    {
        private const double FrameBudgetMs = 2.0;
        private const int CheckEvery = 64;

        /// <summary>
        /// Containers and loose items farther than this from every pod are left out: they cannot belong to a
        /// base (the dashboard's rule is the nearest base within 100 m), and this keeps the file small. It also
        /// drops the hidden storage where launched rockets sit.
        /// </summary>
        private const float ReachMeters = 120f;

        // Cells for merging loose items (same kind, same few metres) into one entry with a count.
        private const float LooseCellMeters = 4f;
        private const int MaxLooseEntries = 4000;

        private sealed class Tally
        {
            public Group Group;
            public int Count;
            public int Ready;
        }

        private readonly int _planetHash;
        private readonly string _planetId;

        private readonly List<string> _pods = new List<string>();
        private readonly List<string> _signs = new List<string>();
        private readonly List<string> _containers = new List<string>();
        private readonly List<string> _extractors = new List<string>();
        private readonly List<Vector2> _podFlat = new List<Vector2>();
        private readonly Dictionary<string, KeyValuePair<Vector3, Tally>> _loose = new Dictionary<string, KeyValuePair<Vector3, Tally>>();

        private readonly Stopwatch _frame = new Stopwatch();
        private double _workMs, _worstMs;
        private int _frames, _visited;
        private bool _looseCapped;

        public WorldScan(string planetId, int planetHash)
        {
            _planetId = planetId;
            _planetHash = planetHash;
        }

        /// <summary>The scan as a coroutine. Run it to the end, then call <see cref="ToJson"/>.</summary>
        public IEnumerator Run()
        {
            var handler = WorldObjectsHandler.Instance;

            // Pass 1: placed constructions (pods, signs, extractors, and the things that might hold items).
            var constructed = Snapshot(handler == null ? null : handler.GetConstructedWorldObjects());
            var candidates = new List<WorldObject>();

            _frame.Restart();
            for (var i = 0; i < constructed.Length; i++)
            {
                VisitConstructed(constructed[i], candidates);

                if ((i % CheckEvery) == CheckEvery - 1 && _frame.Elapsed.TotalMilliseconds >= FrameBudgetMs)
                {
                    EndChunk();
                    yield return null;
                    _frame.Restart();
                }
            }

            // Pass 2: containers close enough to a pod to matter (the pods are all known now).
            for (var i = 0; i < candidates.Count; i++)
            {
                VisitContainer(candidates[i]);

                if ((i % CheckEvery) == CheckEvery - 1 && _frame.Elapsed.TotalMilliseconds >= FrameBudgetMs)
                {
                    EndChunk();
                    yield return null;
                    _frame.Restart();
                }
            }

            // Pass 3: everything, for loose items lying on the ground near a pod.
            var all = Snapshot(handler == null ? null : handler.GetAllWorldObjects());
            for (var i = 0; i < all.Length; i++)
            {
                VisitLoose(all[i]);

                if ((i % CheckEvery) == CheckEvery - 1 && _frame.Elapsed.TotalMilliseconds >= FrameBudgetMs)
                {
                    EndChunk();
                    yield return null;
                    _frame.Restart();
                }
            }

            EndChunk();
        }

        private void EndChunk()
        {
            var ms = _frame.Elapsed.TotalMilliseconds;
            _workMs += ms;
            _worstMs = Math.Max(_worstMs, ms);
            _frames++;
        }

        // A copy, so the game adding or removing objects while this waits for the next frame cannot disturb the walk.
        private static WorldObject[] Snapshot(ICollection<WorldObject> source)
        {
            if (source == null)
                return new WorldObject[0];

            try
            {
                var copy = new WorldObject[source.Count];
                source.CopyTo(copy, 0);
                return copy;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("world:snapshot", $"Could not list the game's objects: {e.GetType().Name}: {e.Message}");
                return new WorldObject[0];
            }
        }

        private static WorldObject[] Snapshot(Dictionary<int, WorldObject> source)
        {
            if (source == null)
                return new WorldObject[0];

            try
            {
                var copy = new WorldObject[source.Count];
                source.Values.CopyTo(copy, 0);
                return copy;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("world:snapshot", $"Could not list the game's objects: {e.GetType().Name}: {e.Message}");
                return new WorldObject[0];
            }
        }

        // ------------------------------------------------------------------ visiting

        private void VisitConstructed(WorldObject o, List<WorldObject> containerCandidates)
        {
            try
            {
                _visited++;
                var group = o?.GetGroup();
                if (group == null || !o.GetIsPlaced() || !OnThisPlanet(o))
                    return;

                var id = group.GetId();
                var kind = ExtractorKind(id);

                if (kind != null)
                    _extractors.Add(ExtractorJson(o, id, kind));
                else if (IsPod(id))
                    AddPod(o, id);
                else if (id.Equals("Sign", StringComparison.OrdinalIgnoreCase))
                    AddSign(o);
                else if (o.HasLinkedInventory() || HasSecondary(o))
                    containerCandidates.Add(o);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("world:constructed", $"Could not read a placed object: {e.GetType().Name}: {e.Message}");
            }
        }

        private void VisitContainer(WorldObject o)
        {
            try
            {
                var position = o.GetPosition();
                if (!NearAPod(position))
                    return;

                var items = Tallies(LinkedInventory(o), false);
                var secondary = Tallies(SecondaryInventories(o), true);
                if (items == null && secondary == null)
                    return;

                _containers.Add(new Json().Begin()
                    .Int("id", o.GetId())
                    .Str("group", o.GetGroup().GetId())
                    .Point("position", position.x, position.y, position.z)
                    .Raw("items", ItemsJson(items))
                    .Raw("secondary", ItemsJson(secondary))
                    .End().ToString());
            }
            catch (Exception e)
            {
                Plugin.LogOnce("world:container", $"Could not read a container: {e.GetType().Name}: {e.Message}");
            }
        }

        private void VisitLoose(WorldObject o)
        {
            try
            {
                // Only things the player (or a machine) put in the world: the game numbers everything that is part of the
                // landscape itself (rocks, wreck loot) below 200,000,000, and those are not "loose items in a base".
                if (_looseCapped || !(o?.GetGroup() is GroupItem) || !o.GetIsPlaced()
                    || WorldObjectsIdHandler.IsWorldObjectFromScene(o.GetId()) || !OnThisPlanet(o))
                    return;

                var position = o.GetPosition();
                if (!NearAPod(position))
                    return;

                var group = o.GetGroup();
                var key = group.GetId() + "|" + Mathf.Floor(position.x / LooseCellMeters) + "|" + Mathf.Floor(position.z / LooseCellMeters);

                if (_loose.TryGetValue(key, out var found))
                {
                    found.Value.Count++;
                    return;
                }

                if (_loose.Count >= MaxLooseEntries)
                {
                    _looseCapped = true;
                    Plugin.LogOnce("world:loose", $"More than {MaxLooseEntries} loose items near the bases; the rest are left out of the world file.");
                    return;
                }

                _loose[key] = new KeyValuePair<Vector3, Tally>(position, new Tally { Group = group, Count = 1 });
            }
            catch (Exception e)
            {
                Plugin.LogOnce("world:loose", $"Could not read a loose item: {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ what things are

        // A living compartment. The dashboard decides which of them are bases (see docs/contract.md).
        private static bool IsPod(string id) =>
            id.StartsWith("pod", StringComparison.OrdinalIgnoreCase) || id.Equals("Escapepod", StringComparison.OrdinalIgnoreCase);

        private static string ExtractorKind(string id)
        {
            if (id.StartsWith("OreExtractor", StringComparison.OrdinalIgnoreCase)) return "ore";
            if (id.StartsWith("GasExtractor", StringComparison.OrdinalIgnoreCase)) return "gas";
            if (id.StartsWith("WaterCollector", StringComparison.OrdinalIgnoreCase)) return "water";
            if (id.StartsWith("AlgaeGenerator", StringComparison.OrdinalIgnoreCase)) return "algae";
            return null;
        }

        private bool OnThisPlanet(WorldObject o)
        {
            var hash = o.GetPlanetHash();
            return _planetHash == 0 || hash == 0 || hash == _planetHash;
        }

        private bool NearAPod(Vector3 position)
        {
            var flat = new Vector2(position.x, position.z);
            var limit = ReachMeters * ReachMeters;

            foreach (var pod in _podFlat)
            {
                if ((pod - flat).sqrMagnitude <= limit)
                    return true;
            }

            return false;
        }

        private void AddPod(WorldObject o, string id)
        {
            var position = o.GetPosition();
            _podFlat.Add(new Vector2(position.x, position.z));

            _pods.Add(new Json().Begin()
                .Int("id", o.GetId())
                .Str("group", id)
                .Point("position", position.x, position.y, position.z)
                .IntList("panels", o.GetPanelsId())
                .End().ToString());
        }

        private void AddSign(WorldObject o)
        {
            var position = o.GetPosition();

            _signs.Add(new Json().Begin()
                .Int("id", o.GetId())
                .Point("position", position.x, position.y, position.z)
                .Str("text", o.GetText())
                .End().ToString());
        }

        // ------------------------------------------------------------------ extractors

        private static string ExtractorJson(WorldObject o, string id, string kind)
        {
            // Algae sit in the machine's secondary storage (with a growth each); the others use the main one.
            var inventory = kind == "algae" ? FirstSecondary(o) : LinkedInventory(o);
            var items = Tallies(inventory, kind == "algae");

            string product = null, productName = null;
            if (kind == "ore" || kind == "gas")
            {
                var groups = o.GetLinkedGroups();
                var chosen = groups != null && groups.Count > 0 ? groups[0] : null;
                if (chosen != null)
                {
                    product = chosen.GetId();
                    productName = InventoryReader.NameOf(product, chosen);
                }
            }

            var count = 0;
            var ready = 0;
            var ofProduct = 0;
            if (items != null)
            {
                foreach (var pair in items)
                {
                    count += pair.Value.Count;
                    ready += pair.Value.Ready;
                    if (pair.Key == product)
                        ofProduct = pair.Value.Count;
                }
            }

            var position = o.GetPosition();

            return new Json().Begin()
                .Int("id", o.GetId())
                .Str("kind", kind)
                .Str("group", id)
                .Point("position", position.x, position.y, position.z)
                .Str("product", product)
                .Str("productName", productName)
                .Int("size", inventory == null ? 0 : inventory.GetSize())
                .Int("count", count)
                .Int("productCount", ofProduct)
                .Int("ready", ready)
                .Raw("items", ItemsJson(items))
                .End().ToString();
        }

        // ------------------------------------------------------------------ inventories

        private static Inventory LinkedInventory(WorldObject o)
        {
            var handler = InventoriesHandler.Instance;
            return handler == null || !o.HasLinkedInventory() ? null : handler.GetInventoryById(o.GetLinkedInventoryId());
        }

        private static bool HasSecondary(WorldObject o)
        {
            var ids = o.GetSecondaryInventoriesId();
            return ids != null && ids.Count > 0;
        }

        private static Inventory FirstSecondary(WorldObject o)
        {
            var handler = InventoriesHandler.Instance;
            return handler == null || !HasSecondary(o) ? null : handler.GetInventoryById(o.GetSecondaryInventoriesId()[0]);
        }

        // All of a machine's secondary inventories, as one. (Growers keep their plants there.)
        private static List<Inventory> SecondaryInventories(WorldObject o)
        {
            var handler = InventoriesHandler.Instance;
            if (handler == null || !HasSecondary(o))
                return null;

            var found = new List<Inventory>();
            foreach (var id in o.GetSecondaryInventoriesId())
            {
                var inventory = handler.GetInventoryById(id);
                if (inventory != null)
                    found.Add(inventory);
            }

            return found;
        }

        private static Dictionary<string, Tally> Tallies(Inventory inventory, bool countReady) =>
            inventory == null ? null : Tallies(new[] { inventory }, countReady);

        /// <summary>Counts the items in some inventories by kind. Null when there is nothing in them.</summary>
        private static Dictionary<string, Tally> Tallies(IEnumerable<Inventory> inventories, bool countReady)
        {
            if (inventories == null)
                return null;

            Dictionary<string, Tally> counts = null;

            foreach (var inventory in inventories)
            {
                foreach (var item in inventory.GetInsideWorldObjects())
                {
                    var group = item?.GetGroup();
                    if (group == null)
                        continue;

                    if (counts == null)
                        counts = new Dictionary<string, Tally>();

                    var id = group.GetId();
                    if (!counts.TryGetValue(id, out var tally))
                        counts[id] = tally = new Tally { Group = group };

                    tally.Count++;

                    // A crop is ready to harvest when it has finished growing (growth is 0 to 100).
                    if (countReady && item.GetGrowth() >= 100f)
                        tally.Ready++;
                }
            }

            return counts;
        }

        private static string ItemsJson(Dictionary<string, Tally> items)
        {
            if (items == null || items.Count == 0)
                return "[]";

            var parts = new List<string>(items.Count);

            foreach (var pair in items)
            {
                var json = new Json().Begin()
                    .Str("id", pair.Key)
                    .Str("name", InventoryReader.NameOf(pair.Key, pair.Value.Group))
                    .Int("count", pair.Value.Count);

                if (pair.Value.Ready > 0)
                    json.Int("ready", pair.Value.Ready);

                parts.Add(json.End().ToString());
            }

            return "[" + string.Join(",", parts) + "]";
        }

        // ------------------------------------------------------------------ result

        public string ToJson(string updatedAt)
        {
            var loose = new List<string>(_loose.Count);
            foreach (var entry in _loose.Values)
            {
                var position = entry.Key;
                var tally = entry.Value;

                loose.Add(new Json().Begin()
                    .Str("id", tally.Group.GetId())
                    .Str("name", InventoryReader.NameOf(tally.Group.GetId(), tally.Group))
                    .Point("position", position.x, position.y, position.z)
                    .Int("count", tally.Count)
                    .End().ToString());
            }

            return new Json().Begin()
                .Int("schemaVersion", 1)
                .Str("pluginVersion", Plugin.Version)
                .Str("updatedAt", updatedAt)
                .Bool("inWorld", true)
                .Str("planetId", _planetId)
                .Begin("scan")
                    .Int("objectsVisited", _visited)
                    .Int("frames", _frames)
                    .Num("workMs", Math.Round(_workMs, 2))
                    .Num("worstFrameMs", Math.Round(_worstMs, 2))
                .End()
                .Raw("pods", Array(_pods))
                .Raw("signs", Array(_signs))
                .Raw("containers", Array(_containers))
                .Raw("loose", Array(loose))
                .Raw("extractors", Array(_extractors))
                .End().ToString();
        }

        private static string Array(List<string> parts) => "[" + string.Join(",", parts) + "]";
    }
}
