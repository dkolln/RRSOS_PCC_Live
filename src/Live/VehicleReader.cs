using System.Collections.Generic;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// The player's trucks (there can be several). A truck's record exists as soon as it is built, whether it is out
    /// in the world or stowed (in the pocket or a portal). Only when it is out does it have a live position.
    /// </summary>
    internal static class VehicleReader
    {
        private const string VehicleGroupId = "VehicleTruck";
        private const float RescanSeconds = 5f;

        private static readonly List<int> VehicleIds = new List<int>();
        private static float _nextScan;

        /// <summary>The "vehicles" list: every truck, oldest first. Empty when the player has none yet.</summary>
        public static string ListFragment()
        {
            var parts = new List<string>();
            foreach (var worldObject in FindAll())
                parts.Add(One(worldObject));

            return "[" + string.Join(",", parts) + "]";
        }

        /// <summary>The older single "vehicle" object: the first truck, or null when there is none. Kept for older readers.</summary>
        public static string Fragment()
        {
            var all = FindAll();
            return all.Count == 0 ? null : One(all[0]);
        }

        private static string One(WorldObject worldObject)
        {
            var json = new Json().Begin().Int("id", worldObject.GetId());

            // The scene object exists only while the vehicle is out in the world.
            var scene = worldObject.GetGameObject();
            if (scene != null && scene.activeInHierarchy)
            {
                var position = scene.transform.position;
                json.Begin("position").Num("x", position.x).Num("y", position.y).Num("z", position.z).End()
                    .Num("yawDegrees", scene.transform.eulerAngles.y);
            }
            else
            {
                json.Null("position");
            }

            var inventories = InventoriesHandler.Instance;
            Inventory trunk = null, gear = null;

            if (inventories != null)
            {
                if (worldObject.HasLinkedInventory())
                    trunk = inventories.GetInventoryById(worldObject.GetLinkedInventoryId());

                var secondary = worldObject.GetSecondaryInventoriesId();
                if (secondary != null && secondary.Count > 0)
                    gear = inventories.GetInventoryById(secondary[0]);
            }

            json.Raw("trunk", Safe("trunk", trunk))
                .Raw("gear", Safe("vehicle gear", gear));

            return json.End().ToString();
        }

        private static string Safe(string what, Inventory inventory)
        {
            try
            {
                return InventoryReader.Fragment(inventory);
            }
            catch (System.Exception e)
            {
                Plugin.LogOnce("vehicle:" + what, $"Could not read the vehicle's {what}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        // The trucks' world objects, by the ids found in the last scan; the scan (a walk over every object) runs only
        // every few seconds, so a truck built or removed shows up within that time.
        private static List<WorldObject> FindAll()
        {
            var found = new List<WorldObject>();
            var handler = WorldObjectsHandler.Instance;
            if (handler == null)
                return found;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + RescanSeconds;
                VehicleIds.Clear();

                foreach (var worldObject in handler.GetAllWorldObjects().Values)
                {
                    var group = worldObject?.GetGroup();
                    if (group != null && group.GetId() == VehicleGroupId)
                        VehicleIds.Add(worldObject.GetId());
                }

                // Oldest first (ids are handed out in order), so "Truck 1" stays Truck 1.
                VehicleIds.Sort();
            }

            foreach (var id in VehicleIds)
            {
                var known = handler.GetWorldObjectViaId(id);
                if (known != null)
                    found.Add(known);
            }

            return found;
        }
    }
}
