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
    /// Writes <c>unlocks.json</c> once per game session: every object the game knows (items and buildings alike) and what it takes to unlock it, so the dashboard can
    /// tell what a save has unlocked without the game running. For each group: the world unit and value that unlock it (oxygen 1 000 000 ppq, heat, plants...; the
    /// terraform stage's start value when it unlocks on a stage, with that stage's id), the planets it only unlocks on (none means any), whether it can be used on
    /// every planet, whether it has to be unlocked with a blueprint chip or a message instead, and whether this world has it unlocked right now. The thresholds live
    /// in the game's assets, not in its code and not in a save, so reading the running game is the only way to know them. Read-only, like everything else here
    /// (see docs/contract.md).
    /// </summary>
    internal sealed class UnlockReader : MonoBehaviour
    {
        public static readonly string FilePath = Path.Combine(LiveFile.Folder, "unlocks.json");

        private const float RetrySeconds = 5f;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(RetrySeconds);

                var done = false;
                try
                {
                    done = TryWrite();
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("unlocks", "Could not read the unlock thresholds: " + e.Message);
                }

                if (done)
                    yield break;
            }
        }

        private static bool TryWrite()
        {
            // Wait for a world, as the recipe list does: by then the game has built every group and its unlock info.
            if (Managers.GetManager<UnlockingHandler>() == null)
                return false;

            var all = GroupsHandler.GetAllGroups();
            if (all == null || all.Count == 0)
                return false;

            var entries = new List<string>();
            foreach (var group in all.Where(g => g != null).OrderBy(g => g.GetId(), StringComparer.Ordinal))
            {
                try
                {
                    entries.Add(GroupJson(group));
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("unlocks-one", "Could not read the unlock info of " + group.GetId() + ": " + e.Message);
                }
            }

            var json = new StringBuilder();
            json.Append("{\"schema\":1,\"groups\":[\n").Append(string.Join(",\n", entries.ToArray())).Append("\n]}");

            LiveFile.Write(FilePath, json.ToString());
            Plugin.Log.LogInfo("Wrote " + FilePath + ": the unlock info of " + entries.Count + " objects.");
            return true;
        }

        private static string GroupJson(Group group)
        {
            var info = group.GetUnlockingInfos();
            var data = group.GetGroupData();

            var sb = new StringBuilder();
            sb.Append("{\"id\":").Append(Str(group.GetId()));
            sb.Append(",\"name\":").Append(Str(NameOf(group)));
            sb.Append(",\"kind\":").Append(Str(group is GroupItem ? "item" : group is GroupConstructible ? "building" : "other"));

            if (group is GroupItem item)
                sb.Append(",\"category\":").Append(Str(item.GetItemCategory().ToString()));

            if (info != null)
            {
                // "Null" with no value means it does not unlock on a world unit at all (a blueprint chip, a message or the story); "Terraformation" at 0 means it is there from the start.
                sb.Append(",\"unit\":").Append(Str(info.GetWorldUnit().ToString())).Append(",\"value\":").Append(Num(info.GetUnlockingValue()));

                var planets = info.GetUnlockingInPlanets();
                sb.Append(",\"planets\":[").Append(planets == null ? "" : string.Join(",", planets.Where(p => p != null).Select(p => Str(p.id)).ToArray())).Append("]");
                sb.Append(",\"viaBlueprint\":").Append(info.GetIsUnlockedViaBlueprint() ? "true" : "false");
            }

            if (data != null)
            {
                if (data.terraformStageUnlock != null)
                    sb.Append(",\"stage\":").Append(Str(data.terraformStageUnlock.GetTerraId()));

                sb.Append(",\"usage\":").Append(Str(data.planetUsageType.ToString()));
            }

            // How many slots its own inventory has, and its secondary inventories (a building the dashboard writes into a save needs these to give it the right ones).
            var size = group.GetInventorySize();
            if (size > 0)
                sb.Append(",\"inventory\":").Append(size);

            var secondary = group.GetSecondaryInventoriesSize();
            if (secondary != null && secondary.Count > 0)
                sb.Append(",\"secondary\":[").Append(string.Join(",", secondary.Select(n => n.ToString()).ToArray())).Append("]");

            sb.Append(",\"unlockedNow\":").Append(IsUnlockedNow(group) ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }

        // The game's own test for putting a building in its menus, as in the terraform reader: the stat it unlocks on has reached its value (on a planet where it applies),
        // or it was unlocked for everyone (a blueprint chip, a message).
        private static bool IsUnlockedNow(Group group)
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

        private static string Num(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }
}
