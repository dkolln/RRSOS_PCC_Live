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
        private readonly List<string> _stations = new List<string>();
        private readonly List<string> _structures = new List<string>();
        private readonly List<WorldObject> _structureObjects = new List<WorldObject>();
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

            // Pass 1b: the building pieces' shapes, for the floor plans. Reading a piece's colliders and panels is much
            // more work than the other visits, so the frame budget is checked after every one.
            for (var i = 0; i < _structureObjects.Count; i++)
            {
                VisitStructure(_structureObjects[i]);

                if (_frame.Elapsed.TotalMilliseconds >= FrameBudgetMs)
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

                if (IsStructure(id))
                    _structureObjects.Add(o);

                if (kind != null)
                    _extractors.Add(ExtractorJson(o, id, kind));
                else if (id.StartsWith("DroneStation", StringComparison.OrdinalIgnoreCase))
                    AddDroneStation(o, id);
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
                // Only things the player (or a machine) put in the world. WorldObjectsIdHandler.IsWorldObjectFromScene
                // (confirmed from the game's own source: it's literally "id < 200,000,000", and every id handed out
                // during play is >= 201,000,000) only tells you whether an object has existed since the world was
                // generated — not whether it's an embedded, unminable vein versus a loose chunk sitting on the ground.
                // World generation scatters some ore directly as loose, walk-up-and-grab chunks, which legitimately
                // belong in the boneyard despite having a "from scene" id (confirmed in-game: 14 of 23 already-loose
                // Titanium chunks next to a live base were silently dropped by this check alone). GetIsPlaced() plus
                // GetGroup() is GroupItem already do the real work of telling "lying on the ground" apart from
                // anything else, and the dashboard's own ore/alloy/quartz/rod filter (IsBoneyardMaterial) keeps
                // actual wreck loot and decorative containers out regardless of id, so this check is dropped rather
                // than narrowed.
                if (_looseCapped || !(o?.GetGroup() is GroupItem) || !o.GetIsPlaced() || !OnThisPlanet(o))
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

        /// <summary>
        /// A piece the floor plans draw: pods of every shape, foundations, launch and trade platforms, the vehicle
        /// platform, domes, labs, the T2 aquarium and ladders. (Pods are also reported in "pods", which is what decides
        /// the bases.) The T1 aquarium is furniture that stands inside a pod, so it is left out.
        /// </summary>
        private static bool IsStructure(string id) =>
            IsPod(id)
            || id.StartsWith("Foundation", StringComparison.OrdinalIgnoreCase)
            || id.IndexOf("Platform", StringComparison.OrdinalIgnoreCase) >= 0
            || id.StartsWith("VehicleCrafter", StringComparison.OrdinalIgnoreCase)
            || id.IndexOf("dome", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("lab", StringComparison.OrdinalIgnoreCase) >= 0
            || (id.StartsWith("Aquarium", StringComparison.OrdinalIgnoreCase)
                && !id.Equals("Aquarium1", StringComparison.OrdinalIgnoreCase)
                && !id.StartsWith("AquariumTube", StringComparison.OrdinalIgnoreCase))
            || id.Equals("Ladder", StringComparison.OrdinalIgnoreCase);

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

        // ------------------------------------------------------------------ building pieces

        /// <summary>
        /// A building piece: where it stands, which way it turns, and its shape as the game has it right now. "box" is
        /// the piece's solid colliders measured in its own frame (metres from its position, before its turn by "yaw"),
        /// and "panelBoxes" does the same for each of its wall and floor panels, in the same order as "panels". Both are
        /// null when the piece has no object in the scene to measure.
        /// </summary>
        private void VisitStructure(WorldObject o)
        {
            try
            {
                if (!NearAPod(o.GetPosition()))
                    return;

                var id = o.GetGroup().GetId();
                var position = o.GetPosition();
                var yaw = o.GetRotation().eulerAngles.y;
                var go = o.GetGameObject();

                string box = null, deckBox = null, panelBoxes = null;
                if (go != null)
                {
                    var root = go.transform;
                    position = root.position;
                    yaw = root.rotation.eulerAngles.y;

                    if (LocalBox(root, go, out var min, out var max))
                        box = BoxJson(min, max);

                    if (DeckBox(root, go, out var deckMin, out var deckMax))
                        deckBox = BoxJson(deckMin, deckMax);

                    panelBoxes = PanelBoxesJson(root, go);
                }

                _structures.Add(new Json().Begin()
                    .Int("id", o.GetId())
                    .Str("group", id)
                    .Point("position", position.x, position.y, position.z)
                    .Num("yaw", Math.Round(yaw, 1))
                    .IntList("panels", o.GetPanelsId())
                    .Raw("box", box)
                    .Raw("deckBox", deckBox)
                    .Raw("panelBoxes", panelBoxes)
                    .End().ToString());
            }
            catch (Exception e)
            {
                Plugin.LogOnce("world:structure", $"Could not read a building piece: {e.GetType().Name}: {e.Message}");
            }
        }

        private static string PanelBoxesJson(Transform root, GameObject go)
        {
            var panels = go.GetComponentsInChildren<Panel>();
            if (panels == null || panels.Length == 0)
                return null;

            var parts = new List<string>(panels.Length);
            foreach (var panel in panels)
            {
                var json = new Json().Begin()
                    .Int("type", (int)panel.GetPanelType())
                    .Int("sub", (int)panel.GetSubPanelType())
                    .Bool("ceiling", panel.GetIsCeiling());

                if (LocalBox(root, panel.gameObject, out var min, out var max))
                    json.Point("min", min.x, min.y, min.z).Point("max", max.x, max.y, max.z);
                else
                    json.Null("min").Null("max");

                parts.Add(json.End().ToString());
            }

            return Array(parts);
        }

        private static string BoxJson(Vector3 min, Vector3 max) =>
            new Json().Begin().Point("min", min.x, min.y, min.z).Point("max", max.x, max.y, max.z).End().ToString();

        /// <summary>A collider no taller than this counts as a slab, when looking for a piece's deck.</summary>
        private const float SlabHeight = 2.5f;

        /// <summary>
        /// A piece's deck: its largest flat slab of collider, together with any other slabs whose top is level with it. For
        /// a platform this is what you walk on, without the gantry and whatever else reaches up over it (a launch
        /// platform's full box is 48 by 31 m and 36 m tall). The top of the box is the deck's height.
        /// </summary>
        private static bool DeckBox(Transform root, GameObject target, out Vector3 min, out Vector3 max)
        {
            var slabs = new List<LocalBounds>();
            LocalBounds widest = null;

            foreach (var one in ColliderBoxes(root, target))
            {
                if (one.Max.y - one.Min.y > SlabHeight)
                    continue;

                slabs.Add(one);
                if (widest == null || Area(one) > Area(widest))
                    widest = one;
            }

            var deck = new LocalBounds(root);
            if (widest != null)
            {
                foreach (var slab in slabs)
                {
                    if (Mathf.Abs(slab.Max.y - widest.Max.y) <= 0.3f)
                        deck.Add(slab);
                }
            }

            min = deck.Min;
            max = deck.Max;
            return deck.Found;
        }

        private static float Area(LocalBounds b) => (b.Max.x - b.Min.x) * (b.Max.z - b.Min.z);

        // Each solid collider under the target, as its own box in the root's frame.
        private static IEnumerable<LocalBounds> ColliderBoxes(Transform root, GameObject target)
        {
            foreach (var c in target.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled)
                    continue;

                var one = new LocalBounds(root);
                if (c is BoxCollider b)
                    one.Add(b.transform, b.center, b.size);
                else if (c is MeshCollider m && m.sharedMesh != null)
                    one.Add(m.transform, m.sharedMesh.bounds.center, m.sharedMesh.bounds.size);
                else
                    one.AddWorld(c.bounds);

                if (one.Found)
                    yield return one;
            }
        }

        /// <summary>
        /// The box around <paramref name="target"/>'s solid colliders (or, with none, its meshes), in
        /// <paramref name="root"/>'s frame: metres from its position, before its rotation.
        /// </summary>
        private static bool LocalBox(Transform root, GameObject target, out Vector3 min, out Vector3 max)
        {
            var box = new LocalBounds(root);

            foreach (var one in ColliderBoxes(root, target))
                box.Add(one);

            if (!box.Found)
            {
                foreach (var f in target.GetComponentsInChildren<MeshFilter>())
                {
                    if (f != null && f.sharedMesh != null)
                        box.Add(f.transform, f.sharedMesh.bounds.center, f.sharedMesh.bounds.size);
                }
            }

            min = box.Min;
            max = box.Max;
            return box.Found;
        }

        /// <summary>Grows a box, in one piece's own frame, around boxes found anywhere under it.</summary>
        private sealed class LocalBounds
        {
            private readonly Quaternion _inverse;
            private readonly Vector3 _origin;

            public LocalBounds(Transform root)
            {
                _inverse = Quaternion.Inverse(root.rotation);
                _origin = root.position;
            }

            public bool Found { get; private set; }
            public Vector3 Min { get; private set; }
            public Vector3 Max { get; private set; }

            // A box in some child's own frame: its eight corners go through the child's transform into the world, then into the root's frame.
            public void Add(Transform t, Vector3 center, Vector3 size)
            {
                var h = size * 0.5f;
                for (var i = 0; i < 8; i++)
                {
                    var corner = center + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                    Point(t.TransformPoint(corner));
                }
            }

            // Another box already in the same root's frame.
            public void Add(LocalBounds other)
            {
                if (!other.Found)
                    return;

                PointLocal(other.Min);
                PointLocal(other.Max);
            }

            public void AddWorld(Bounds b)
            {
                for (var i = 0; i < 8; i++)
                    Point(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
            }

            private void Point(Vector3 world) => PointLocal(_inverse * (world - _origin));

            private void PointLocal(Vector3 local)
            {
                if (!Found)
                {
                    Min = Max = local;
                    Found = true;
                }
                else
                {
                    Min = Vector3.Min(Min, local);
                    Max = Vector3.Max(Max, local);
                }
            }
        }

        // A drone station and what is in its storage (the drones docked in it, mostly). Its drones that are flying are in live.json.
        private void AddDroneStation(WorldObject o, string id)
        {
            var position = o.GetPosition();
            var inventory = LinkedInventory(o);
            var items = Tallies(inventory, false);

            var docked = 0;
            if (items != null)
            {
                foreach (var pair in items)
                {
                    if (IsDroneGroup(pair.Key))
                        docked += pair.Value.Count;
                }
            }

            _stations.Add(new Json().Begin()
                .Int("id", o.GetId())
                .Str("group", id)
                .Str("name", InventoryReader.NameOf(id, o.GetGroup()))
                .Point("position", position.x, position.y, position.z)
                .Int("size", inventory == null ? 0 : inventory.GetSize())
                .Int("docked", docked)
                .Raw("items", ItemsJson(items))
                .End().ToString());
        }

        // "Drone", "Drone1", "Drone2"...: a drone, as opposed to a station or the rocket that carries drones.
        private static bool IsDroneGroup(string id)
        {
            if (!id.StartsWith("Drone", StringComparison.OrdinalIgnoreCase))
                return false;

            for (var i = "Drone".Length; i < id.Length; i++)
            {
                if (!char.IsDigit(id[i]))
                    return false;
            }

            return true;
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
                .Raw("droneStations", Array(_stations))
                .Raw("structures", Array(_structures))
                .End().ToString();
        }

        private static string Array(List<string> parts) => "[" + string.Join(",", parts) + "]";
    }
}
