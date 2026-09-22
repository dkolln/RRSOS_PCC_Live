using System;
using System.Collections.Generic;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// The drones that are in the air. A drone in a station has no place in the world (it is an item in the station's
    /// storage, which the world file reports), but one that is flying is a live scene object with a real position, a
    /// task, and cargo in its own storage. Drones are found with the game's own components and re-listed every few
    /// seconds; between lists only their positions and tasks are read, which is what changes.
    /// </summary>
    internal static class DroneReader
    {
        private const float RescanSeconds = 5f;

        private static Drone[] _drones = new Drone[0];
        private static float _nextScan;

        /// <summary>The "drones" object: {"flying":[...]}, or null when it could not be read.</summary>
        public static string Fragment()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + RescanSeconds;
                _drones = UnityEngine.Object.FindObjectsByType<Drone>(FindObjectsSortMode.None);
            }

            var planetHash = PlanetHash();
            var flying = new List<string>();

            foreach (var drone in _drones)
            {
                try
                {
                    var json = One(drone, planetHash);
                    if (json != null)
                        flying.Add(json);
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("drone", $"Could not read a drone: {e.GetType().Name}: {e.Message}");
                }
            }

            return new Json().Begin().Raw("flying", "[" + string.Join(",", flying) + "]").End().ToString();
        }

        private static string One(Drone drone, int planetHash)
        {
            // Destroyed since the last list, or put away in a station (inactive): not in the air.
            if (drone == null || !drone.gameObject.activeInHierarchy)
                return null;

            var worldObject = drone.GetComponent<WorldObjectAssociated>()?.GetWorldObject();
            if (worldObject == null)
                return null;

            // A drone on another planet keeps its own hash; zero means the game has not stamped it yet.
            var hash = worldObject.GetPlanetHash();
            if (planetHash != 0 && hash != 0 && hash != planetHash)
                return null;

            var transform = drone.transform;
            var group = worldObject.GetGroup();
            var task = drone.GetLogisticTask();

            var json = new Json().Begin()
                .Int("id", worldObject.GetId())
                .Str("group", group == null ? null : group.GetId())
                .Str("name", group == null ? null : InventoryReader.NameOf(group.GetId(), group))
                .Point("position", transform.position.x, transform.position.y, transform.position.z)
                .Num("yawDegrees", transform.eulerAngles.y)
                .Num("speed", drone.forwardSpeed)
                .Str("state", task == null ? "Returning" : task.GetTaskState().ToString());

            json.Raw("cargo", Safe("cargo", () => InventoryReader.Fragment(drone.GetDroneInventory())));

            if (task != null)
            {
                json.Raw("moving", Safe("task item", () => Group(task.GetWorldObjectToMove())))
                    .Raw("from", Safe("task source", () => Place(task.GetSupplyInventoryWorldObject())))
                    .Raw("to", Safe("task target", () => Place(task.GetDemandInventoryWorldObject())));
            }
            else
            {
                json.Null("moving").Null("from").Null("to");
            }

            return json.End().ToString();
        }

        // {"id":"Iron","name":"Iron"} for what the drone was sent to carry.
        private static string Group(WorldObject worldObject)
        {
            var group = worldObject == null ? null : worldObject.GetGroup();
            return group == null ? null : new Json().Begin().Str("id", group.GetId()).Str("name", InventoryReader.NameOf(group.GetId(), group)).End().ToString();
        }

        // Where it picks up from or delivers to: what that machine is, and where it stands.
        private static string Place(WorldObject worldObject)
        {
            if (worldObject == null)
                return null;

            var group = worldObject.GetGroup();
            var scene = worldObject.GetGameObject();
            var position = scene != null ? scene.transform.position : worldObject.GetPosition();

            return new Json().Begin()
                .Str("id", group == null ? null : group.GetId())
                .Str("name", group == null ? null : InventoryReader.NameOf(group.GetId(), group))
                .Point("position", position.x, position.y, position.z)
                .End().ToString();
        }

        private static string Safe(string what, Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                Plugin.LogOnce("drone:" + what, $"Could not read a drone's {what}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        private static int PlanetHash()
        {
            var loader = Managers.GetManager<PlanetLoader>();
            var data = loader == null ? null : loader.GetCurrentPlanetData();
            return data == null ? 0 : data.GetPlanetHash();
        }
    }
}
