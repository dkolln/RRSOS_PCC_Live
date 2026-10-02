using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Writes <c>pins.json</c> whenever the recipes pinned to the top right of the screen (the microchip's pin) change: for each pin, the object's id and name and what
    /// its recipe takes, with the ingredients' names. The dashboard turns each new pin into a note, so the recipe is still there once the pin is cleared. The game keeps
    /// the pins in a private list of its pin canvas, so that is read by reflection; nothing is changed. Read-only, like everything else here (see docs/contract.md).
    /// </summary>
    internal sealed class PinReader : MonoBehaviour
    {
        public static readonly string FilePath = Path.Combine(LiveFile.Folder, "pins.json");

        private const float PollSeconds = 0.5f;

        private static readonly FieldInfo GroupsField = typeof(CanvasPinedRecipes).GetField("_groupsAdded", BindingFlags.Instance | BindingFlags.NonPublic);

        private CanvasPinedRecipes _canvas;
        private string _written;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(PollSeconds);

                try
                {
                    Poll();
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("pins", "Could not read the pinned recipes: " + e.Message);
                }
            }
        }

        private void Poll()
        {
            if (GroupsField == null)
            {
                Plugin.LogOnce("pins-field", "The game's pin list was not found; pinned recipes will not be noted.");
                return;
            }

            if (_canvas == null)
                _canvas = FindFirstObjectByType<CanvasPinedRecipes>(FindObjectsInactive.Include);

            // No pin canvas means no world (the main menu): write nothing, so the last pins stay as they were.
            if (_canvas == null)
                return;

            var groups = (GroupsField.GetValue(_canvas) as IEnumerable ?? new List<Group>()).OfType<Group>().Where(g => g != null).ToList();
            var json = Json(groups);

            if (json == _written)
                return;

            LiveFile.Write(FilePath, json);
            _written = json;
        }

        private static string Json(List<Group> groups)
        {
            var pins = groups.Select(PinJson).ToArray();
            return "{\"schema\":1,\"pins\":[" + string.Join(",", pins) + "]}";
        }

        private static string PinJson(Group group)
        {
            // The same ingredient listed twice means two of it.
            var counts = new List<KeyValuePair<Group, int>>();
            foreach (var ingredient in group.GetRecipe()?.GetIngredientsGroupInRecipe() ?? new List<Group>())
            {
                if (ingredient == null)
                    continue;

                var at = counts.FindIndex(c => c.Key.GetId() == ingredient.GetId());
                if (at < 0) counts.Add(new KeyValuePair<Group, int>(ingredient, 1));
                else counts[at] = new KeyValuePair<Group, int>(ingredient, counts[at].Value + 1);
            }

            var sb = new StringBuilder();
            sb.Append("{\"id\":").Append(Str(group.GetId()));
            sb.Append(",\"name\":").Append(Str(Readable.GetGroupName(group)));
            sb.Append(",\"ingredients\":[").Append(string.Join(",", counts.Select(c =>
                "{\"id\":" + Str(c.Key.GetId()) + ",\"name\":" + Str(Readable.GetGroupName(c.Key)) + ",\"count\":" + c.Value + "}").ToArray())).Append("]}");
            return sb.ToString();
        }

        private static string Str(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
