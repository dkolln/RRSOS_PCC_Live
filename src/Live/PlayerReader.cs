using System;
using System.Collections.Generic;
using System.Reflection;
using SpaceCraft;
using Unity.Netcode;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>What the game says about the local player right now.</summary>
    internal sealed class PlayerState
    {
        public PlayerMainController Controller;
        public string Name;
        public string Planet;
        public Vector3 Position;

        /// <summary>The body's yaw as Unity reports it: 0 is straight along +Z, turning clockwise seen from above.</summary>
        public float YawDegrees;

        public float Oxygen;
        public float Health;
        public float Thirst;
        public float Toxic;

        /// <summary>The top of each gauge (the game keeps these in private fields). Null when they could not be read.</summary>
        public float? OxygenMax, HealthMax, ThirstMax, ToxicMax;
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
                Controller = player,
                Name = player.playerName,
                Planet = ReadPlanetId(),
                Position = transform.position,
                YawDegrees = transform.eulerAngles.y,
                Oxygen = gauges.GetPlayerOxygenValue(),
                Health = gauges.GetPlayerHealthValue(),
                Thirst = gauges.GetPlayerThirstValue(),
                Toxic = gauges.GetPlayerToxicValue(),
                OxygenMax = MaxOf(gauges, "_oxygenMaxValue"),
                HealthMax = MaxOf(gauges, "_healthMaxValue"),
                ThirstMax = MaxOf(gauges, "_thirstMaxValue"),
                ToxicMax = MaxOf(gauges, "_toxicMaxValue")
            };
        }

        /// <summary>The backpack, as an inventory fragment (see <see cref="InventoryReader"/>).</summary>
        public static string BackpackFragment(PlayerState player)
        {
            var backpack = player.Controller.GetPlayerBackpack();
            return backpack == null ? null : InventoryReader.Fragment(backpack.GetInventory());
        }

        /// <summary>Worn gear (boots, jetpack, chips and so on), as an inventory fragment.</summary>
        public static string EquipmentFragment(PlayerState player)
        {
            var equipment = player.Controller.GetPlayerEquipment();
            return equipment == null ? null : InventoryReader.Fragment(equipment.GetInventory());
        }

        // The gauge maximums are private network variables on the game's gauge handler.
        private static readonly Dictionary<string, FieldInfo> MaxFields = new Dictionary<string, FieldInfo>();

        private static float? MaxOf(PlayerGaugesHandler gauges, string fieldName)
        {
            try
            {
                if (!MaxFields.TryGetValue(fieldName, out var field))
                {
                    field = typeof(PlayerGaugesHandler).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                    MaxFields[fieldName] = field;
                }

                var variable = field == null ? null : field.GetValue(gauges) as NetworkVariable<float>;
                return variable == null ? (float?)null : variable.Value;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("max:" + fieldName, $"Could not read {fieldName}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        private static string ReadPlanetId()
        {
            var loader = Managers.GetManager<PlanetLoader>();
            return loader == null ? null : loader.GetCurrentPlanetData()?.GetPlanetId();
        }
    }
}
