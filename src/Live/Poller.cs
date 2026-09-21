using System;
using System.Globalization;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Once a second, reads the game and rewrites the live file. Polling (instead of patching game code)
    /// keeps this read-only and means a game update cannot silently break a hook.
    /// </summary>
    internal sealed class Poller : MonoBehaviour
    {
        private const float IntervalSeconds = 1f;
        private const int MaxLoggedErrors = 5;

        private float _nextTick;
        private bool? _wasInWorld;
        private int _errors;

        private void Update()
        {
            if (Time.unscaledTime < _nextTick)
                return;

            _nextTick = Time.unscaledTime + IntervalSeconds;
            Tick();
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
                // Never let a reading problem reach the game. Say so a few times, then stay quiet.
                if (_errors++ < MaxLoggedErrors)
                    Plugin.Log.LogWarning($"Could not update the live file: {e.GetType().Name}: {e.Message}");
            }
        }

        private static string Build(PlayerState player)
        {
            var json = new Json().Begin()
                .Int("schemaVersion", 0)
                .Str("pluginVersion", Plugin.Version)
                .Str("gameVersion", Application.version)
                .Str("updatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Bool("inWorld", player != null);

            if (player != null)
            {
                json.Str("planet", player.Planet)
                    .Begin("player")
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
                    .End();
            }

            return json.End().ToString();
        }
    }
}
