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
    /// Writes <c>recipes.json</c> once per game session: how the game makes every object. For each group (item or building) that has a recipe, its ingredients
    /// (with counts), where it can be crafted (the game's <c>CraftableIn</c> list), its category and how it unlocks; and for each machine that crafts, which
    /// crafting place it is, its craft time and, for an autocrafter, its range and how often it crafts. The data lives in the game's assets, not in its code and
    /// not in a save, so reading the running game is the only way to know it. Read-only, like everything else here (see docs/contract.md).
    /// </summary>
    internal sealed class RecipeReader : MonoBehaviour
    {
        public static readonly string FilePath = Path.Combine(LiveFile.Folder, "recipes.json");

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
                    Plugin.LogOnce("recipes", "Could not read the recipes: " + e.Message);
                }

                if (done)
                    yield break;
            }
        }

        private static bool TryWrite()
        {
            // Wait for a world, as the blueprint list does: by then the game has built every group and its recipe.
            if (Managers.GetManager<UnlockingHandler>() == null)
                return false;

            var all = GroupsHandler.GetAllGroups();
            if (all == null || all.Count == 0)
                return false;

            var recipes = new List<string>();
            var machines = new List<string>();

            foreach (var group in all.Where(g => g != null).OrderBy(g => g.GetId(), StringComparer.Ordinal))
            {
                var ingredients = group.GetRecipe()?.GetIngredientsGroupInRecipe();
                var items = ingredients == null ? new List<Group>() : ingredients.Where(i => i != null).ToList();

                if (items.Count > 0)
                    recipes.Add(RecipeJson(group, items));

                var machine = MachineJson(group);
                if (machine != null)
                    machines.Add(machine);
            }

            var json = new StringBuilder();
            json.Append("{\"schema\":1,\"recipes\":[\n").Append(string.Join(",\n", recipes.ToArray()));
            json.Append("\n],\"machines\":[\n").Append(string.Join(",\n", machines.ToArray())).Append("\n]}");

            LiveFile.Write(FilePath, json.ToString());
            Plugin.Log.LogInfo("Wrote " + FilePath + ": " + recipes.Count + " recipes, " + machines.Count + " crafting machines.");
            return true;
        }

        private static string RecipeJson(Group group, List<Group> ingredients)
        {
            // The same ingredient listed twice means two of it.
            var counts = new List<KeyValuePair<string, int>>();
            foreach (var ingredient in ingredients)
            {
                var id = ingredient.GetId();
                var at = counts.FindIndex(c => c.Key == id);
                if (at < 0) counts.Add(new KeyValuePair<string, int>(id, 1));
                else counts[at] = new KeyValuePair<string, int>(id, counts[at].Value + 1);
            }

            var sb = new StringBuilder();
            sb.Append("{\"id\":").Append(Str(group.GetId()));
            sb.Append(",\"kind\":").Append(Str(group is GroupItem ? "item" : group is GroupConstructible ? "building" : "other"));
            sb.Append(",\"ingredients\":{").Append(string.Join(",", counts.Select(c => Str(c.Key) + ":" + c.Value).ToArray())).Append("}");

            if (group is GroupItem item)
            {
                sb.Append(",\"category\":").Append(Str(item.GetItemCategory().ToString()));
                sb.Append(",\"craftableIn\":[").Append(string.Join(",", (item.GetCraftableInList() ?? new List<DataConfig.CraftableIn>()).Select(c => Str(c.ToString())).ToArray())).Append("]");
            }

            sb.Append(",\"hideInCrafter\":").Append(group.GetHideInCrafter() ? "true" : "false");

            var data = group.GetGroupData();
            if (data != null)
                sb.Append(",\"unlock\":{\"unit\":").Append(Str(data.unlockingWorldUnit.ToString())).Append(",\"value\":").Append(Num(data.unlockingValue)).Append("}");

            sb.Append("}");
            return sb.ToString();
        }

        // A machine that crafts: the crafting place it is (CraftableIn), its craft time, and for an autocrafter its range and how often it crafts.
        private static string MachineJson(Group group)
        {
            GameObject prefab;
            try { prefab = group.GetAssociatedGameObject(); }
            catch (Exception) { return null; }

            if (prefab == null)
                return null;

            var crafter = prefab.GetComponentInChildren<ActionCrafter>(true);
            var auto = prefab.GetComponentInChildren<MachineAutoCrafter>(true);
            var convert = prefab.GetComponentInChildren<MachineConvertRecipe>(true);

            if (crafter == null && auto == null && convert == null)
                return null;

            var sb = new StringBuilder();
            sb.Append("{\"id\":").Append(Str(group.GetId()));

            if (crafter != null)
                sb.Append(",\"crafts\":").Append(Str(crafter.craftableIdentifier.ToString())).Append(",\"craftTime\":").Append(Num(crafter.craftTime));

            if (auto != null)
                sb.Append(",\"autocrafter\":{\"range\":").Append(Num(auto.range)).Append(",\"craftEverySec\":").Append(Num(auto.craftEveryXSec)).Append("}");

            if (convert != null)
                sb.Append(",\"convertsRecipe\":true");

            sb.Append("}");
            return sb.ToString();
        }

        private static string Str(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string Num(float v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
