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
            json.Append("],\"loot\":[").Append(loot);

            // The two other ways the game unlocks a group outright, neither in the tier lists: a chip item that points at one group (BlueprintContainer3,
            // BlueprintPod9xA, ...) and a story event that unlocks one when it fires.
            json.Append("],\"itemUnlocks\":[").Append(ItemUnlocks(all));
            json.Append("],\"storyUnlocks\":[").Append(StoryUnlocks());
            json.Append("]}");

            LiveFile.Write(FilePath, json.ToString());
            Plugin.Log.LogInfo("Wrote " + FilePath + ": " + tiers.Sum(t => t == null ? 0 : t.Count) + " groups behind blueprints, plus the item-linked chips and story unlocks.");
            return true;
        }

        private static string ItemUnlocks(List<Group> all)
        {
            var rows = new List<string>();
            if (all == null)
                return "";

            foreach (var group in all)
            {
                try
                {
                    var item = group as GroupItem;
                    var target = item == null ? null : item.GetUnlocksGroup();
                    if (target != null)
                        rows.Add("{\"item\":\"" + item.GetId() + "\",\"group\":\"" + target.GetId() + "\"}");
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("blueprints-item", "Could not read what " + (group == null ? "?" : group.GetId()) + " unlocks: " + e.Message);
                }
            }

            return string.Join(",", rows.ToArray());
        }

        // Every story event of every planet the build has (not only the current one), once each, that unlocks a group when it is received.
        private static string StoryUnlocks()
        {
            var rows = new List<string>();
            try
            {
                var loader = Managers.GetManager<PlanetLoader>();
                var planets = loader == null || loader.planetList == null ? null : loader.planetList.GetPlanetList(true);
                var seen = new HashSet<string>();
                if (planets != null)
                {
                    foreach (var planet in planets.Where(p => p != null))
                    {
                        var events = new List<StoryEventData>();
                        if (planet.availableStoryEvents != null) events.AddRange(planet.availableStoryEvents);
                        if (planet.manualStoryEvents != null) events.AddRange(planet.manualStoryEvents);
                        if (planet.sharedManualEvents != null && planet.sharedManualEvents.GetStoryEvents() != null) events.AddRange(planet.sharedManualEvents.GetStoryEvents());

                        foreach (var ev in events.Where(e => e != null && e.GetUnlockGroupOnReceive() != null))
                        {
                            if (seen.Add(ev.id))
                                rows.Add("{\"event\":\"" + ev.id + "\",\"planet\":\"" + planet.id + "\",\"group\":\"" + ev.GetUnlockGroupOnReceive().id + "\"}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogOnce("blueprints-story", "Could not read the story events that unlock groups: " + e.Message);
            }

            return string.Join(",", rows.ToArray());
        }

        private static string Ids(List<GroupData> groups) =>
            groups == null
                ? ""
                : string.Join(",", groups.Where(g => g != null).Select(g => "\"" + g.id + "\"").ToArray());
    }
}