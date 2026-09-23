using System;
using System.Collections.Generic;
using System.Globalization;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Which way each Transmission Antenna's dish points, for live.json. The dish is a part called Radar_Base_01 that
    /// the game spins with its Turn_Move script: about its own vertical axis at a steady rate (50 degrees a second,
    /// clockwise from above; found with a probe in the game). A reading gives each dish's compass heading, the rate
    /// it turns at (0 while the game is paused), and the exact moment it was read, so the dashboard can keep turning it
    /// smoothly between readings. The antennas' parts are looked up again every few seconds, not every reading.
    /// </summary>
    internal static class AntennaReader
    {
        private const float RefindSeconds = 10f;

        private sealed class Dish
        {
            public int Id;
            public Vector3 Position;
            public Transform Part;
            public Turn_Move Turner;
        }

        private static List<Dish> _dishes = new List<Dish>();
        private static float _foundAt = float.NegativeInfinity;

        public static string Fragment()
        {
            if (Time.realtimeSinceStartup - _foundAt > RefindSeconds || _dishes.Exists(d => d.Part == null))
            {
                _dishes = Find();
                _foundAt = Time.realtimeSinceStartup;
            }

            var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var parts = new List<string>(_dishes.Count);

            foreach (var dish in _dishes)
            {
                if (dish.Part == null)
                    continue;

                var turning = dish.Turner != null && dish.Turner.enabled && dish.Part.gameObject.activeInHierarchy;
                var rate = turning ? dish.Turner.TurnY * Time.timeScale : 0f;

                parts.Add(new Json().Begin()
                    .Int("id", dish.Id)
                    .Point("position", dish.Position.x, dish.Position.y, dish.Position.z)
                    .Num("heading", Math.Round(Bearing(dish.Part.forward), 1))
                    .Num("degreesPerSecond", Math.Round(rate, 2))
                    .Raw("sampledAtMs", at)
                    .End().ToString());
            }

            return "[" + string.Join(",", parts) + "]";
        }

        private static List<Dish> Find()
        {
            var found = new List<Dish>();
            var handler = WorldObjectsHandler.Instance;
            if (handler == null)
                return found;

            foreach (var o in handler.GetConstructedWorldObjects())
            {
                var id = o?.GetGroup()?.GetId();
                if (id == null || !id.StartsWith("ComAntenna", StringComparison.OrdinalIgnoreCase))
                    continue;

                var go = o.GetGameObject();
                var turner = go == null ? null : go.GetComponentInChildren<Turn_Move>(true);
                if (turner == null)
                    continue;

                found.Add(new Dish { Id = o.GetId(), Position = go.transform.position, Part = turner.transform, Turner = turner });
            }

            return found;
        }

        // Compass bearing of a direction on the ground: north is world +X, east is world -Z (measured in the game).
        private static float Bearing(Vector3 v)
        {
            var bearing = Mathf.Atan2(-v.z, v.x) * Mathf.Rad2Deg;
            return bearing < 0 ? bearing + 360f : bearing;
        }
    }
}
