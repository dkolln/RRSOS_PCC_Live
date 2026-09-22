using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Once a second, reads the game and rewrites the live file. Polling (instead of patching game code)
    /// keeps this read-only and means a game update cannot silently break a hook. Every section is read
    /// on its own, so one that fails becomes null and the others still arrive.
    /// </summary>
    internal sealed class Poller : MonoBehaviour
    {
        private const float IntervalSeconds = 1f;

        private float _nextTick;
        private bool? _wasInWorld;

        private void Update()
        {
            if (Time.unscaledTime < _nextTick)
                return;

            _nextTick = Time.unscaledTime + IntervalSeconds;
            Tick();
        }

        // Closing the game while in a world would leave "inWorld: true" behind. Say it is over.
        private void OnApplicationQuit()
        {
            if (_wasInWorld != true)
                return;

            try
            {
                LiveFile.Write(Build(null));
            }
            catch (Exception)
            {
                // The game is closing; readers fall back on updatedAt going stale.
            }
        }

        private void Tick()
        {
            try
            {
                var player = PlayerReader.Read();
                var inWorld = player != null;

                // Out of a world the file says so once, instead of being rewritten every second.
                if (!inWorld && _wasInWorld == false)
                    return;

                if (inWorld != _wasInWorld)
                    Plugin.Log.LogInfo(inWorld
                        ? $"In world: {player.Planet}. Writing {LiveFile.FilePath} once a second."
                        : "Not in a world. The live file now says so.");

                _wasInWorld = inWorld;
                LiveFile.Write(Build(player));
            }
            catch (Exception e)
            {
                Plugin.LogOnce("tick", $"Could not update the live file: {e.GetType().Name}: {e.Message}");
            }
        }

        private static string Build(PlayerState player)
        {
            var json = new Json().Begin()
                .Int("schemaVersion", 1)
                .Str("pluginVersion", Plugin.Version)
                .Str("gameVersion", Application.version)
                .Str("updatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Bool("inWorld", player != null);

            if (player != null)
            {
                json.Str("planetId", player.Planet)
                    .Raw("player", Section("player", () => PlayerSection(player)))
                    .Raw("planet", Section("planet", PlanetReader.Fragment))
                    .Raw("vehicle", Section("vehicle", VehicleReader.Fragment))
                    .Raw("drones", Section("drones", DroneReader.Fragment));
            }

            return json.End().ToString();
        }

        private static string PlayerSection(PlayerState player)
        {
            return new Json().Begin()
                .Str("name", player.Name)
                .Begin("position")
                    .Num("x", player.Position.x)
                    .Num("y", player.Position.y)
                    .Num("z", player.Position.z)
                .End()
                .Num("yawDegrees", player.YawDegrees)
                .Begin("vitals")
                    .Num("oxygen", player.Oxygen)
                    .Num("health", player.Health)
                    .Num("thirst", player.Thirst)
                    .Num("toxic", player.Toxic)
                .End()
                .Begin("vitalsMax")
                    .OptNum("oxygen", player.OxygenMax)
                    .OptNum("health", player.HealthMax)
                    .OptNum("thirst", player.ThirstMax)
                    .OptNum("toxic", player.ToxicMax)
                .End()
                .Raw("backpack", Section("backpack", () => PlayerReader.BackpackFragment(player)))
                .Raw("equipment", Section("equipment", () => PlayerReader.EquipmentFragment(player)))
                .End()
                .ToString();
        }

        /// <summary>Runs one section's reader. A failure is logged (once per kind) and the section becomes null.</summary>
        private static string Section(string name, Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                Plugin.LogOnce("section:" + name, $"Could not read '{name}': {e.GetType().Name}: {e.Message}");
                return null;
            }
        }
    }
}
