using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The shopping list: every recipe the player pins in the game (the microchip's pin, top right) is added here, with its ingredients, and stays after the pin is
    /// cleared, so the game's few pin slots are no limit. Reads the plugin's <c>pins.json</c> (plugin 0.11.0) and keeps the list in <c>shopping-list.json</c> beside it.
    /// An entry is added when an id appears that was not pinned the last time the file was read; pins already there when this app starts are not added, so a restart
    /// adds nothing twice. Pinning something already on the list does not add it again (change its quantity instead).
    /// </summary>
    public sealed class ShoppingListService : BackgroundService
    {
        private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(1000);

        private readonly ILogger<ShoppingListService> _log;
        private readonly string _pinsPath;
        private readonly string _listPath;
        private readonly DateTime _startedUtc = DateTime.UtcNow;
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        private readonly object _lock = new();
        private readonly ShoppingFile _file = new();
        private DateTime _lastWriteUtc = DateTime.MinValue;
        private long _lastLength = -1;
        private HashSet<string>? _previous;

        public ShoppingListService(ILogger<ShoppingListService> log, IConfiguration config)
        {
            _log = log;
            _pinsPath = Path.Combine(LivePaths.Folder(config), "pins.json");
            _listPath = Path.Combine(LivePaths.Folder(config), "shopping-list.json");
            Load();
        }

        /// <summary>Raised when the list changes. Handlers must hop onto their own thread.</summary>
        public event Action? Changed;

        /// <summary>The list as it stands now (a copy).</summary>
        public IReadOnlyList<ShoppingEntry> Items()
        {
            lock (_lock)
                return _file.Items.Select(e => new ShoppingEntry
                {
                    Id = e.Id, Name = e.Name, Qty = e.Qty, Added = e.Added,
                    Ingredients = e.Ingredients.Select(i => new ShoppingIngredient { Id = i.Id, Name = i.Name, Count = i.Count }).ToList()
                }).ToList();
        }

        /// <summary>What the whole list takes: each ingredient summed over the entries, times their quantities, by name.</summary>
        public static IReadOnlyList<ShoppingIngredient> Totals(IEnumerable<ShoppingEntry> items) =>
            items.SelectMany(e => e.Ingredients.Select(i => (i.Id, i.Name, Count: i.Count * e.Qty)))
                .GroupBy(t => t.Id, StringComparer.Ordinal)
                .Select(g => new ShoppingIngredient { Id = g.Key, Name = g.First().Name, Count = g.Sum(t => t.Count) })
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        /// <summary>
        /// Adds one single item (a fuse, say) by hand, next to the recipes pinned in the game. It is its own ingredient, so it lands in the totals by itself and
        /// adds up with the same item wanted by a recipe; with a <paramref name="breakdown"/> it is what the item takes to make instead, as for a pinned recipe.
        /// Adding one already on the list wants one more.
        /// </summary>
        public void AddItem(string id, string name, IReadOnlyList<ShoppingIngredient>? breakdown = null) =>
            Edit(f =>
            {
                if (string.IsNullOrWhiteSpace(id))
                    return false;

                if (f.Items.FirstOrDefault(e => e.Id == id) is { } existing)
                {
                    existing.Qty++;

                    // Asking for the breakdown of something added as a plain item gives it its ingredients.
                    if (breakdown is { Count: > 0 })
                        existing.Ingredients = breakdown.Select(i => new ShoppingIngredient { Id = i.Id, Name = i.Name, Count = i.Count }).ToList();

                    return true;
                }

                f.Items.Add(new ShoppingEntry
                {
                    Id = id, Name = string.IsNullOrWhiteSpace(name) ? id : name, Qty = 1, Added = DateTime.UtcNow,
                    Ingredients = breakdown is { Count: > 0 }
                        ? breakdown.Select(i => new ShoppingIngredient { Id = i.Id, Name = i.Name, Count = i.Count }).ToList()
                        : new List<ShoppingIngredient> { new() { Id = id, Name = string.IsNullOrWhiteSpace(name) ? id : name, Count = 1 } }
                });
                return true;
            });

        public void Remove(string id) => Edit(f => f.Items.RemoveAll(e => e.Id == id) > 0);

        public void Clear() => Edit(f => { var any = f.Items.Count > 0; f.Items.Clear(); return any; });

        public void SetQty(string id, int qty) =>
            Edit(f =>
            {
                var entry = f.Items.FirstOrDefault(e => e.Id == id);
                if (entry is null || qty < 1 || entry.Qty == qty)
                    return false;

                entry.Qty = qty;
                return true;
            });

        private void Edit(Func<ShoppingFile, bool> change)
        {
            lock (_lock)
            {
                if (!change(_file))
                    return;

                Save();
            }

            Changed?.Invoke();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_listPath) && JsonSerializer.Deserialize<ShoppingFile>(File.ReadAllText(_listPath), LiveJson.Options) is { } loaded)
                    _file.Items = loaded.Items;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _log.LogWarning(e, "Could not read the shopping list at {Path}; starting with an empty one", _listPath);
            }
        }

        // Called with the lock held.
        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_listPath)!);
            File.WriteAllText(_listPath, JsonSerializer.Serialize(_file, _json));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Poll();
                }
                catch (Exception e)
                {
                    _log.LogWarning(e, "Could not add the pinned recipes to the shopping list");
                }

                await Task.Delay(PollEvery, stoppingToken);
            }
        }

        private void Poll()
        {
            var info = new FileInfo(_pinsPath);
            if (!info.Exists || (info.LastWriteTimeUtc == _lastWriteUtc && info.Length == _lastLength))
                return;

            var pins = Read();
            if (pins is null)
                return;

            _lastWriteUtc = info.LastWriteTimeUtc;
            _lastLength = info.Length;

            // The first reading is the baseline, unless the game wrote it after this app started (then every pin in it is new).
            _previous ??= info.LastWriteTimeUtc < _startedUtc ? pins.Select(p => p.Id).ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);

            var added = false;
            lock (_lock)
            {
                foreach (var pin in pins.Where(p => !_previous.Contains(p.Id) && _file.Items.All(e => e.Id != p.Id)))
                {
                    _file.Items.Add(new ShoppingEntry
                    {
                        Id = pin.Id, Name = pin.Name, Qty = 1, Added = DateTime.UtcNow,
                        Ingredients = pin.Ingredients.Select(i => new ShoppingIngredient { Id = i.Id, Name = i.Name, Count = i.Count }).ToList()
                    });
                    added = true;
                }

                if (added)
                    Save();
            }

            _previous = pins.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

            if (added)
                Changed?.Invoke();
        }

        private List<Pin>? Read()
        {
            try
            {
                using var stream = new FileStream(_pinsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<PinFile>(stream, LiveJson.Options)?.Pins ?? new List<Pin>();
            }
            catch (IOException)
            {
                return null; // being replaced right now; the next poll gets it
            }
            catch (JsonException e)
            {
                _log.LogWarning("The pins file is not valid JSON: {Message}", e.Message);
                return null;
            }
        }

        private sealed class PinFile
        {
            public List<Pin> Pins { get; set; } = new();
        }

        private sealed class Pin
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public List<ShoppingIngredient> Ingredients { get; set; } = new();
        }
    }
}
