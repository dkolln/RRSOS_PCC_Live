using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>The broad kinds of thing, as the thousands digit of an <see cref="ItemType"/>. Copied from RRSOS-PCC.</summary>
    public enum ItemCategory
    {
        None = 0,
        Equipment = 1000,
        Resource = 2000,
        Biological = 3000,
        Consumable = 4000,
        Machine = 5000,
        BasePart = 6000,
        Component = 7000,
        Modifier = 8000,
        Container = 9000,
        WorldMarker = 10000,
        UtilityItem = 11000,
        Wreck = 12000,
        Unknown = 9999
    }

    /// <summary>
    /// Says what kind of thing a game object is, from a table copied from RRSOS-PCC (Assets/worldobjectdata.json).
    /// The game has no such grouping that is fine enough to show; this is the one the save-file dashboard uses, so
    /// "Group by Type" and "Group by Category" read the same in both. Only the type is used from that table: names
    /// come from the game itself.
    /// </summary>
    public sealed class ItemCatalog
    {
        private static readonly Regex TrailingDigits = new(@"\d+$", RegexOptions.Compiled);

        private sealed class Row
        {
            public int Type { get; set; }
        }

        private readonly Dictionary<string, ItemType> _types = new(StringComparer.OrdinalIgnoreCase);

        public ItemCatalog(IWebHostEnvironment env, ILogger<ItemCatalog> log)
        {
            var path = Path.Combine(env.ContentRootPath, "Assets", "worldobjectdata.json");

            try
            {
                using var stream = File.OpenRead(path);
                var rows = JsonSerializer.Deserialize<Dictionary<string, Row>>(stream, LiveJson.Options);

                foreach (var pair in rows ?? new())
                    _types[pair.Key] = Enum.IsDefined(typeof(ItemType), pair.Value.Type) ? (ItemType)pair.Value.Type : ItemType.Unknown;
            }
            catch (Exception e)
            {
                // Without the table everything is "Unknown" but the page still works.
                log.LogWarning(e, "Could not read {Path}; items will not be grouped by type or category", path);
            }
        }

        /// <summary>The type for a game group id: the exact id, else the id without its trailing digits ("Iron1" is "Iron"), else Unknown.</summary>
        public ItemType TypeOf(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
                return ItemType.Unknown;

            if (_types.TryGetValue(groupId, out var exact))
                return exact;

            return _types.TryGetValue(TrailingDigits.Replace(groupId, ""), out var stripped) ? stripped : ItemType.Unknown;
        }

        public ItemCategory CategoryOf(string groupId) => CategoryOf(TypeOf(groupId));

        public static ItemCategory CategoryOf(ItemType type)
        {
            var category = (int)type / 1000 * 1000;
            return Enum.IsDefined(typeof(ItemCategory), category) ? (ItemCategory)category : ItemCategory.Unknown;
        }
    }
}
