using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Writes <c>terraformers.json</c> every few seconds while a world is loaded: for each planet stat (oxygen, heat, pressure, plants, insects, animals, purification),
    /// every building the game has that makes it, with how many of them stand on this planet, what one makes, what they make together right now (each machine's own
    /// live figure, boosts included, the same one the power card uses) and whether the player has unlocked it. The dashboard shows it when a gauge is clicked.
    /// The base figure comes from the game's building data (<c>GroupConstructible.GetGroupUnitGeneration</c>) and the live one from each placed machine
    /// (<c>WorldObject.GetUnitGeneration</c>); "unlocked" is the game's own test for showing a building in its menus. Read-only, like everything else here
    /// (see docs/contract.md).
    /// </summary>
    internal sealed class TerraformReader : MonoBehaviour
    {
        public static readonly string FilePath = Path.Combine(LiveFile.Folder, "terraformers.json");

        private const float EverySeconds = 3f;

        private static readonly KeyValuePair<string, DataConfig.WorldUnitType>[] Units =
        {
            new KeyValuePair<string, DataConfig.WorldUnitType>("oxygen", DataConfig.WorldUnitType.Oxygen),
            new KeyValuePair<string, DataConfig.WorldUnitType>("heat", DataConfig.WorldUnitType.Heat),
            new KeyValuePair<string, DataConfig.WorldUnitType>("pressure", DataConfig.WorldUnitType.Pressure),
            new KeyValuePair<string, DataConfig.WorldUnitType>("plants", DataConfig.WorldUnitType.Plants),
            new KeyValuePair<string, DataConfig.WorldUnitType>("insects", DataConfig.WorldUnitType.Insects),
            new KeyValuePair<string, DataConfig.WorldUnitType>("animals", DataConfig.WorldUnitType.Animals),
            new KeyValuePair<string, DataConfig.WorldUnitType>("purification", DataConfig.WorldUnitType.Purification)
        };

        private sealed class Live
        {
            public int Count;   // every one built here that can make the stat, working or not
            public int Active;  // those making some of it right now
            public double Total;
        }

        private string _written;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(EverySeconds);

                try
                {
                    var json = Build();
                    if (json != null && json != _written)
                    {
                        LiveFile.Write(FilePath, json);
                        _written = json;
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("terraformers", "Could not read what makes each planet stat: " + e.Message);
                }
            }
        }

        // Null when no world is loaded (the main menu): nothing is written then, so the last reading stays.
        private static string Build()
        {
            var planetHash = PlanetReader.PlanetHash();
            var groups = GroupsHandler.GetAllGroups();
            if (planetHash == 0 || groups == null || groups.Count == 0)
                return null;

            // What is standing on this planet and making each stat right now, by building.
            var live = new Dictionary<string, Dictionary<string, Live>>();
            foreach (var unit in Units)
                live[unit.Key] = new Dictionary<string, Live>();

            var constructed = WorldObjectsHandler.Instance == null ? null : WorldObjectsHandler.Instance.GetConstructedWorldObjects();
            if (constructed != null)
            {
                foreach (var worldObject in constructed)
                {
                    if (worldObject == null || !PlanetReader.OnThisPlanet(worldObject, planetHash) || !(worldObject.GetGroup() is GroupConstructible group))
                        continue;

                    var id = group.GetId();
                    foreach (var unit in Units)
                    {
                        // A planter, spreader or the like makes its stat only while it holds something: empty, the game reports 0 for it. It is still built here, so it is counted,
                        // and told apart from the ones working by Active.
                        var value = worldObject.GetUnitGeneration(unit.Value);
                        if (value <= 0f && group.GetGroupUnitGeneration(unit.Value) <= 0f)
                            continue;

                        var byId = live[unit.Key];
                        if (!byId.TryGetValue(id, out var entry))
                            byId[id] = entry = new Live();

                        entry.Count++;
                        if (value > 0f)
                            entry.Active++;

                        entry.Total += value;
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\"schema\":1,\"planetHash\":").Append(planetHash).Append(",\"units\":{");

            for (var u = 0; u < Units.Length; u++)
            {
                var unit = Units[u];
                var entries = new List<string>();

                foreach (var group in groups.OfType<GroupConstructible>())
                {
                    var each = group.GetGroupUnitGeneration(unit.Value);
                    live[unit.Key].TryGetValue(group.GetId(), out var made);

                    // Everything that can make the stat, built or not; and anything built that makes it (a boost can make a building that normally does not).
                    if (each <= 0f && made == null)
                        continue;

                    var count = made == null ? 0 : made.Count;
                    var active = made == null ? 0 : made.Active;
                    var total = made == null ? 0.0 : made.Total;

                    entries.Add("{\"id\":" + Str(group.GetId()) + ",\"name\":" + Str(NameOf(group)) + ",\"count\":" + count + ",\"active\":" + active
                        + ",\"each\":" + Num(each) + ",\"total\":" + Num((float)total) + ",\"unlocked\":" + (count > 0 || IsUnlocked(group) ? "true" : "false") + "}");
                }

                sb.Append(u == 0 ? "" : ",").Append(Str(unit.Key)).Append(":[").Append(string.Join(",", entries.ToArray())).Append("]");
            }

            return sb.Append("}}").ToString();
        }

        // The game's own test for putting a building in its menus: the stat it unlocks on has reached its value (on a planet where it applies), or it was unlocked
        // for everyone (a blueprint chip, a message).
        private static bool IsUnlocked(Group group)
        {
            try
            {
                var infos = group.GetUnlockingInfos();
                return (infos != null && infos.GetIsUnlocked(true)) || group.GetIsGloballyUnlocked();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string NameOf(Group group)
        {
            try
            {
                var name = Readable.GetGroupName(group);
                return string.IsNullOrEmpty(name) ? group.GetId() : name;
            }
            catch (Exception)
            {
                return group.GetId();
            }
        }

        private static string Str(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string Num(float v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }
}
