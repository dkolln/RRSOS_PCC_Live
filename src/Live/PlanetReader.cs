using System.Collections.Generic;
using SpaceCraft;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// The planet's stats straight from the game's own world units: each one's total, and what it gains
    /// and loses per second. The game has already applied every machine, optimizer and rocket to those
    /// rates, so nothing here is calculated. For energy, "increase" is power produced and "decrease"
    /// (a negative number) is power used, both in kW.
    /// </summary>
    internal static class PlanetReader
    {
        private static readonly KeyValuePair<string, DataConfig.WorldUnitType>[] Units =
        {
            new KeyValuePair<string, DataConfig.WorldUnitType>("oxygen", DataConfig.WorldUnitType.Oxygen),
            new KeyValuePair<string, DataConfig.WorldUnitType>("heat", DataConfig.WorldUnitType.Heat),
            new KeyValuePair<string, DataConfig.WorldUnitType>("pressure", DataConfig.WorldUnitType.Pressure),
            new KeyValuePair<string, DataConfig.WorldUnitType>("plants", DataConfig.WorldUnitType.Plants),
            new KeyValuePair<string, DataConfig.WorldUnitType>("insects", DataConfig.WorldUnitType.Insects),
            new KeyValuePair<string, DataConfig.WorldUnitType>("animals", DataConfig.WorldUnitType.Animals),
            new KeyValuePair<string, DataConfig.WorldUnitType>("biomass", DataConfig.WorldUnitType.Biomass),
            new KeyValuePair<string, DataConfig.WorldUnitType>("terraformation", DataConfig.WorldUnitType.Terraformation),
            new KeyValuePair<string, DataConfig.WorldUnitType>("purification", DataConfig.WorldUnitType.Purification),
            new KeyValuePair<string, DataConfig.WorldUnitType>("energy", DataConfig.WorldUnitType.Energy)
        };

        private sealed class Rocket
        {
            public int Count;
            public float Multiplier;
        }

        /// <summary>The "planet" object, or null while the world's units are not ready.</summary>
        public static string Fragment()
        {
            var handler = Managers.GetManager<WorldUnitsHandler>();
            if (handler == null || !handler.AreUnitsInited())
                return null;

            var json = new Json().Begin().Begin("units");

            double produced = 0, used = 0;

            foreach (var pair in Units)
            {
                var unit = handler.GetUnit(pair.Value);
                if (unit == null)
                    continue;

                var increase = unit.GetIncreaseValuePersSec();
                var decrease = unit.GetDecreaseValuePersSec();

                json.Begin(pair.Key)
                    .Num("value", unit.GetValue())
                    .Num("increasePerSec", increase)
                    .Num("decreasePerSec", decrease)
                    .End();

                if (pair.Value == DataConfig.WorldUnitType.Energy)
                {
                    produced = increase;
                    used = -decrease;
                }
            }

            json.End()
                .Raw("power", PowerFragment(produced, used))
                .Raw("rockets", RocketsFragment());

            return json.End().ToString();
        }

        private sealed class Generator
        {
            public int Count;
            public double Kw;
        }

        // Totals from the game, plus what each kind of generator is producing right now (each machine's own
        // live output, optimizer boosts included), so a reader can draw the generators without calculating.
        private static string PowerFragment(double produced, double used)
        {
            var json = new Json().Begin().Num("producedKw", produced).Num("usedKw", used).BeginArray("generators");

            var constructed = WorldObjectsHandler.Instance == null ? null : WorldObjectsHandler.Instance.GetConstructedWorldObjects();
            var byId = new Dictionary<string, Generator>();

            if (constructed != null)
            {
                foreach (var worldObject in constructed)
                {
                    if (worldObject == null)
                        continue;

                    var kw = worldObject.GetUnitGeneration(DataConfig.WorldUnitType.Energy);
                    if (kw <= 0f)
                        continue;

                    var id = worldObject.GetGroup() == null ? null : worldObject.GetGroup().GetId();
                    if (id == null)
                        continue;

                    if (!byId.TryGetValue(id, out var generator))
                        byId[id] = generator = new Generator();

                    generator.Count++;
                    generator.Kw += kw;
                }
            }

            foreach (var pair in byId)
                json.Begin().Str("id", pair.Key).Int("count", pair.Value.Count).Num("kw", pair.Value.Kw).End();

            return json.EndArray().End().ToString();
        }

        // Launched rockets sit in hidden "space" storage objects (one per stat). The game adds up their
        // multipliers itself when it works out a stat's rate, so read the same numbers it does.
        private static string RocketsFragment()
        {
            var handler = WorldObjectsHandler.Instance;
            var inventories = InventoriesHandler.Instance;
            var constructed = handler == null ? null : handler.GetConstructedWorldObjects();
            if (constructed == null)
                return null;

            var byStat = new Dictionary<string, Rocket>();

            foreach (var worldObject in constructed)
            {
                if (worldObject == null || !(worldObject.GetGroup() is GroupConstructible group))
                    continue;

                var type = group.GetWorldUnitMultiplied();
                if (type == DataConfig.WorldUnitType.Null)
                    continue;

                var key = type.ToString().ToLowerInvariant();
                if (!byStat.TryGetValue(key, out var rocket))
                    byStat[key] = rocket = new Rocket();

                rocket.Multiplier += worldObject.GetUnitMultiplier(type);

                if (inventories != null && worldObject.HasLinkedInventory())
                {
                    var inventory = inventories.GetInventoryById(worldObject.GetLinkedInventoryId());
                    if (inventory != null)
                        rocket.Count += inventory.GetInsideWorldObjects().Count;
                }
            }

            var json = new Json().Begin();

            foreach (var pair in byStat)
                json.Begin(pair.Key).Int("count", pair.Value.Count).Num("multiplier", pair.Value.Multiplier).End();

            return json.End().ToString();
        }
    }
}
