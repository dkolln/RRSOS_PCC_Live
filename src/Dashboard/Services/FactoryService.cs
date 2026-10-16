namespace RRSOS.PCC.Dashboard
{
    /// <summary>The recipe book the plugin writes (recipes.json), plus a few name helpers for the live Factory views.</summary>
    public sealed class FactoryService
    {
        private readonly ItemCatalog _catalog;
        private readonly IConfiguration _config;

        // Book() is polled once a second by the Home page (through WorldFileService), so the parsed result is kept
        // until the file's write time or length changes rather than re-parsing 90-odd KB of JSON every time.
        private readonly object _bookLock = new();
        private DateTime _bookWriteUtc;
        private long _bookLength = -1;
        private RecipeBook? _book;

        public FactoryService(ItemCatalog catalog, IConfiguration config)
        {
            _catalog = catalog;
            _config = config;
        }

        public static string BookPath(IConfiguration config) => Path.Combine(LivePaths.Folder(config), "recipes.json");

        /// <summary>The game's recipes as the plugin wrote them, or null when the file is not there yet (start the game once with plugin 0.9.0 or later).</summary>
        public RecipeBook? Book()
        {
            try
            {
                var info = new FileInfo(BookPath(_config));
                if (!info.Exists)
                    return null;

                lock (_bookLock)
                {
                    if (_book is not null && info.LastWriteTimeUtc == _bookWriteUtc && info.Length == _bookLength)
                        return _book;

                    using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    _book = RecipeBook.Parse(reader.ReadToEnd());
                    _bookWriteUtc = info.LastWriteTimeUtc;
                    _bookLength = info.Length;
                    return _book;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }


        /// <summary>The name a sign shows for a product: the item's name, unless that is missing or shared with another product, then its id.</summary>
        public Func<string, string> LabelOf(IEnumerable<string> products)
        {
            var list = products.Distinct(StringComparer.Ordinal).ToList();
            var names = list.ToDictionary(p => p, p => _catalog.NameOf(p), StringComparer.Ordinal);
            var shared = names.GroupBy(n => n.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return product =>
            {
                var name = names.TryGetValue(product, out var n) ? n : _catalog.NameOf(product);
                return string.IsNullOrWhiteSpace(name) || string.Equals(name, product, StringComparison.OrdinalIgnoreCase) || shared.Contains(name) ? product : name.Replace("\"", "");
            };
        }

        public string NameOf(string id) => _catalog.NameOf(id);

    }
}
