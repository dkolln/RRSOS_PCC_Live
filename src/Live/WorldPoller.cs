using System;
using System.Collections;
using System.Globalization;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Bases, containers and extractors change slowly and there can be thousands of them, so they do not go in the
    /// once-a-second file. This writes a second file, <c>live-world.json</c>, every few seconds, and does the work
    /// a little at a time over several frames (see <see cref="WorldScan"/>) so the game never notices.
    /// </summary>
    internal sealed class WorldPoller : MonoBehaviour
    {
        private const float IntervalSeconds = 5f;

        private bool? _wasInWorld;

        // Unity runs a Start that returns an IEnumerator as a coroutine.
        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(IntervalSeconds);

                var player = ReadPlayer();

                if (player == null)
                {
                    SayNotInWorld();
                    continue;
                }

                _wasInWorld = true;

                var scan = new WorldScan(player.Planet, PlanetHash());

                // A failure inside the scan is caught piece by piece; this only guards the file writing.
                yield return StartCoroutine(scan.Run());

                try
                {
                    LiveFile.Write(LiveFile.WorldFilePath, scan.ToJson(Now()));
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("world:write", $"Could not write the world file: {e.GetType().Name}: {e.Message}");
                }
            }
        }

        // Out of a world the file says so once, instead of being rewritten every few seconds.
        private void SayNotInWorld()
        {
            if (_wasInWorld == false)
                return;

            _wasInWorld = false;

            try
            {
                LiveFile.Write(LiveFile.WorldFilePath, new Json().Begin()
                    .Int("schemaVersion", 1)
                    .Str("pluginVersion", Plugin.Version)
                    .Str("updatedAt", Now())
                    .Bool("inWorld", false)
                    .End().ToString());
            }
            catch (Exception e)
            {
                Plugin.LogOnce("world:write", $"Could not write the world file: {e.GetType().Name}: {e.Message}");
            }
        }

        private static PlayerState ReadPlayer()
        {
            try
            {
                return PlayerReader.Read();
            }
            catch (Exception e)
            {
                Plugin.LogOnce("world:player", $"Could not tell whether a world is loaded: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        private static int PlanetHash()
        {
            try
            {
                var loader = Managers.GetManager<PlanetLoader>();
                return loader == null || loader.GetCurrentPlanetData() == null ? 0 : loader.GetCurrentPlanetData().GetPlanetHash();
            }
            catch (Exception e)
            {
                Plugin.LogOnce("world:planet", $"Could not read the planet's hash: {e.GetType().Name}: {e.Message}");
                return 0;
            }
        }

        private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
