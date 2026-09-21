using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// The player's truck. Its record exists as soon as it is unlocked, whether it is out in the world or
    /// stowed (in the pocket or a portal). Only when it is out does it have a live position.
    /// </summary>
    internal static class VehicleReader
    {
        private const string VehicleGroupId = "VehicleTruck";
        private const float RescanSeconds = 5f;

        private static int _vehicleId;
        private static float _nextScan;

        /// <summary>The "vehicle" object, or null when the player has no vehicle yet.</summary>
        public static string Fragment()
        {
            var worldObject = Find();
            if (worldObject == null)
                return null;

            var json = new Json().Begin();

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

        // Finds the truck's world object once and remembers it; while there is none, look again every few seconds.
        private static WorldObject Find()
        {
            var handler = WorldObjectsHandler.Instance;
            if (handler == null)
                return null;

            if (_vehicleId != 0)
            {
                var known = handler.GetWorldObjectViaId(_vehicleId);
                if (known != null)
                    return known;

                _vehicleId = 0;
            }

            if (Time.unscaledTime < _nextScan)
                return null;

            _nextScan = Time.unscaledTime + RescanSeconds;

            foreach (var worldObject in handler.GetAllWorldObjects().Values)
            {
                var group = worldObject?.GetGroup();
                if (group != null && group.GetId() == VehicleGroupId)
                {
                    _vehicleId = worldObject.GetId();
                    return worldObject;
                }
            }

            return null;
        }
    }
}
