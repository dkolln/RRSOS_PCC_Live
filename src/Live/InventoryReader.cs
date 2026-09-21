using System.Collections.Generic;
using SpaceCraft;

namespace RRSOS.PCC.Live
{
    /// <summary>Turns a game inventory into a JSON fragment: its size and the items in it, counted by kind.</summary>
    internal static class InventoryReader
    {
        // Item display names are localized text; look each kind up once.
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>();

        /// <summary>{"size":N,"items":[{"id":"Iron","name":"Iron","count":12},...]}, or null for no inventory.</summary>
        public static string Fragment(Inventory inventory)
        {
            if (inventory == null)
                return null;

            var counts = new Dictionary<string, int>();
            var groups = new Dictionary<string, Group>();

            foreach (var worldObject in inventory.GetInsideWorldObjects())
            {
                var group = worldObject?.GetGroup();
                if (group == null)
                    continue;

                var id = group.GetId();
                counts.TryGetValue(id, out var have);
                counts[id] = have + 1;
                groups[id] = group;
            }

            var json = new Json().Begin().Int("size", inventory.GetSize()).BeginArray("items");

            foreach (var pair in counts)
            {
                json.Begin()
                    .Str("id", pair.Key)
                    .Str("name", NameOf(pair.Key, groups[pair.Key]))
                    .Int("count", pair.Value)
                    .End();
            }

            return json.EndArray().End().ToString();
        }

        internal static string NameOf(string id, Group group)
        {
            if (Names.TryGetValue(id, out var cached))
                return cached;

            string name;
            try
            {
                name = Readable.GetGroupName(group);
            }
            catch
            {
                name = null;
            }

            // Fall back on the id if the game has no name for it (or its text system is not ready).
            name = string.IsNullOrEmpty(name) ? id : name;
            Names[id] = name;
            return name;
        }
    }
}
