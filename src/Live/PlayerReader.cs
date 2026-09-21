using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>What the game says about the local player right now.</summary>
    internal sealed class PlayerState
    {
        public string Name;
        public string Planet;
        public Vector3 Position;

        /// <summary>The body's yaw as Unity reports it: 0 is straight along +Z, turning clockwise seen from above.</summary>
        public float YawDegrees;

        public float Oxygen;
        public float Health;
        public float Thirst;
        public float Toxic;
    }

    /// <summary>Read-only access to the local player. Returns null when no world is loaded.</summary>
    internal static class PlayerReader
    {
        public static PlayerState Read()
        {
            // The manager is null on the main menu and until a world has finished loading.
            var players = Managers.GetManager<PlayersManager>();
            if (players == null)
                return null;

            var player = players.GetActivePlayerController();
            if (player == null)
                return null;

            var gauges = player.GetPlayerGaugesHandler();
            if (gauges == null || !gauges.HasStarted())
                return null;

            var transform = player.transform;

            return new PlayerState
            {
                Name = player.playerName,
                Planet = ReadPlanetId(),
                Position = transform.position,
                YawDegrees = transform.eulerAngles.y,
                Oxygen = gauges.GetPlayerOxygenValue(),
                Health = gauges.GetPlayerHealthValue(),
                Thirst = gauges.GetPlayerThirstValue(),
                Toxic = gauges.GetPlayerToxicValue()
            };
        }

        private static string ReadPlanetId()
        {
            var loader = Managers.GetManager<PlanetLoader>();
            return loader == null ? null : loader.GetCurrentPlanetData()?.GetPlanetId();
        }
    }
}
