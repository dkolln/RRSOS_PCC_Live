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
    /// Writes <c>blueprints.json</c> once per game session: which buildings the game locks behind blueprints (its own unlock lists, one per tier, and the
    /// ones unlocked by messages) and the group id of the blueprint chip itself. The lists live in the game's data assets, not in its code and not in a
    /// save, so this is the only way to know them. Read-only, like everything else here: it reads the game's <c>UnlockingHandler</c> and writes a file.
    /// The dashboard's Base Building uses it to put one blueprint chip for each still-locked building into a chest (see docs/contract.md).
    /// </summary>
    internal sealed class BlueprintReader : MonoBehaviour
    {
        public static readonly string FilePath = Path.Combine(LiveFile.Folder, "blueprints.json");

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
                    Plugin.LogOnce("blueprints", "Could not read the blueprint unlock lists: " + e.Message);
                }

                if (done)
                    yield break;
            }
        }

        private static bool TryWrite()
        {
            var handler = Managers.GetManager<UnlockingHandler>();
            if (handler == null || handler.unlockingData == null)
                return false; // no world loaded yet

            var data = handler.unlockingData;
            var tiers = new[]
            {
                data.tier1GroupToUnlock, data.tier2GroupToUnlock, data.tier3GroupToUnlock, data.tier4GroupToUnlock, data.tier5GroupToUnlock,
                data.tier6GroupToUnlock, data.tier7GroupToUnlock, data.tier8GroupToUnlock, data.tier9GroupToUnlock, data.tier10GroupToUnlock
            };

            var json = new StringBuilder();
            json.Append("{\"chip\":\"").Append(handler.bluePrintChipGroupData != null ? handler.bluePrintChipGroupData.id : "").Append("\",\"tiers\":[");
            json.Append(string.Join(",", tiers.Select(t => "[" + Ids(t) + "]").ToArray()));
            json.Append("],\"messages\":[").Append(Ids(data.groupToUnlockInMessages));

            // The buildings and objects that drop a blueprint chip linked to themselves when they are deconstructed while still locked (the exercise bike,
            // the treadmill, ...): they are not in the tier lists above, they are found by taking them apart.
            var all = GroupsHandler.GetAllGroups();
            var loot = all == null
                ? ""
                : string.Join(",", all.Where(g => g != null && g.GetLootRecipeOnDeconstruct()).Select(g => "\"" + g.GetId() + "\"").ToArray());
            json.Append("],\"loot\":[").Append(loot).Append("]}");

            LiveFile.Write(FilePath, json.ToString());
            Plugin.Log.LogInfo("Wrote " + FilePath + ": " + tiers.Sum(t => t == null ? 0 : t.Count) + " groups behind blueprints.");
            return true;
        }

        private static string Ids(List<GroupData> groups) =>
            groups == null
                ? ""
                : string.Join(",", groups.Where(g => g != null).Select(g => "\"" + g.id + "\"").ToArray());
    }
}