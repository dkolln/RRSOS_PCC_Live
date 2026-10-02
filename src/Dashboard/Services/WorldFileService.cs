using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Watches the plugin's world file (bases, containers, extractors: it changes every few seconds, not every second)
    /// and keeps the latest reading, with the bases already worked out from it. Like <see cref="LiveFileService"/>, it
    /// does not care whether the game or this app started first. The bases' boneyards come from the last save instead
    /// (see <see cref="SaveLooseService"/>), so the bases are worked out again when either changes.
    /// </summary>
    public sealed class WorldFileService : BackgroundService
    {
        private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(1000);

        /// <summary>How loud a rocket announcement speaks, independent of whatever the Home page's own slider is set to right
        /// now: this fires from the background, whether or not a browser tab is even open.</summary>
        private const double RocketAlertVolume = 0.9;

        /// <summary>Guards against saying the same rocket's state twice from one blip (the file is read every second, but a
        /// departure or arrival is a one-off edge, so this is just insurance, not the usual repeat-suppression.</summary>
        private static readonly TimeSpan RocketAlertCooldown = TimeSpan.FromSeconds(20);

        private static readonly IReadOnlyDictionary<string, string> RocketNames = new Dictionary<string, string>
        {
            ["trade"] = "Trade Rocket",
            ["interplanetary"] = "Interplanetary Rocket"
        };

        private readonly ILogger<WorldFileService> _log;
        private readonly BaseNames _names;
        private readonly ItemCatalog _catalog;
        private readonly SaveLooseService _save;
        private readonly FactoryService _factory;
        private readonly SpeechService _speech;
        private readonly string _path;
        private readonly object _buildLock = new();
        private DateTime _lastWriteUtc = DateTime.MinValue;
        private long _lastLength = -1;

        // Whether each rocket (by its platform's id) was docked last time this was read, so a change can be told apart
        // from "this is the first time we've looked". Cleared of ids the current reading no longer has.
        private readonly Dictionary<int, bool> _rocketOnSite = new();

        public WorldFileService(ILogger<WorldFileService> log, IConfiguration config, BaseNames names, ItemCatalog catalog, SaveLooseService save, FactoryService factory, SpeechService speech)
        {
            _log = log;
            _names = names;
            _catalog = catalog;
            _save = save;
            _factory = factory;
            _speech = speech;
            _path = LivePaths.WorldFile(config);
            _save.Changed += OnSaveRead;
        }

        // A newer save was read: the same world reading, with the boneyards from that save.
        private void OnSaveRead()
        {
            if (Data is { } data)
            {
                SetBases(data);
                Changed?.Invoke();
            }
        }

        private void SetBases(WorldData data)
        {
            // On the main menu the plugin says "not in a world" and nothing else: that is no bases, not a failure.
            // Book() is cached (see FactoryService), so this costs nothing extra beyond a file-time check.
            var watch = System.Diagnostics.Stopwatch.StartNew();

            lock (_buildLock)
                Bases = data.InWorld ? BaseDirectory.Build(data, _save.Objects, _names, _catalog, _factory.Book()) : BaseDirectory.Empty;

            watch.Stop();
            _log.LogDebug("Bases worked out in {Ms} ms ({Containers} containers, {Structures} building pieces)", watch.ElapsedMilliseconds, data.Containers.Count, data.Structures.Count);
        }

        /// <summary>The latest world reading, or null when the plugin has not written one.</summary>
        public WorldData? Data { get; private set; }

        /// <summary>The bases in that reading. Empty when there is none, or when the game is on the main menu.</summary>
        public BaseDirectory Bases { get; private set; } = BaseDirectory.Empty;

        /// <summary>Raised when a new reading arrives. Handlers must hop onto their own thread.</summary>
        public event Action? Changed;

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
                    _log.LogWarning(e, "Could not read the world file");
                }

                await Task.Delay(PollEvery, stoppingToken);
            }
        }

        private void Poll()
        {
            var info = new FileInfo(_path);

            if (!info.Exists)
            {
                if (Data is not null)
                {
                    Data = null;
                    Bases = BaseDirectory.Empty;
                    _lastWriteUtc = DateTime.MinValue;
                    _lastLength = -1;
                    _rocketOnSite.Clear();
                    Changed?.Invoke();
                }

                return;
            }

            if (info.LastWriteTimeUtc == _lastWriteUtc && info.Length == _lastLength)
                return;

            var parsed = Read();
            if (parsed is null)
                return;

            _lastWriteUtc = info.LastWriteTimeUtc;
            _lastLength = info.Length;

            AnnounceRockets(parsed);
            SetBases(parsed);
            Data = parsed;
            Changed?.Invoke();
        }

        /// <summary>
        /// Speaks when a trade or interplanetary-exchange rocket leaves or comes back (see <see cref="RocketStateData"/>): a
        /// trip the owner has to wait out, so it is worth hearing about even with the dashboard out of sight. Known only
        /// once this has read the world twice with the rocket still there; the very first reading just remembers where it
        /// stood, and an id the reading no longer has (the piece removed, or the save changed planet) is forgotten rather
        /// than treated as "it left".
        /// </summary>
        private void AnnounceRockets(WorldData data)
        {
            var seen = new HashSet<int>();

            foreach (var s in data.Structures)
            {
                if (s.Rocket is not { } rocket || !RocketNames.TryGetValue(rocket.Kind, out var name))
                    continue;

                seen.Add(s.Id);

                if (_rocketOnSite.TryGetValue(s.Id, out var wasOnSite) && wasOnSite != rocket.OnSite)
                {
                    var text = $"{name} {(rocket.OnSite ? "Arriving" : "Departing")}";
                    _speech.Alert("rocket:" + s.Id, text, RocketAlertVolume, RocketAlertCooldown);
                }

                _rocketOnSite[s.Id] = rocket.OnSite;
            }

            foreach (var gone in _rocketOnSite.Keys.Where(id => !seen.Contains(id)).ToList())
                _rocketOnSite.Remove(gone);
        }

        private WorldData? Read()
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<WorldData>(stream, LiveJson.Options)?.Normalize();
            }
            catch (IOException)
            {
                return null; // being replaced right now; the next poll gets it
            }
            catch (JsonException e)
            {
                _log.LogWarning("The world file is not valid JSON: {Message}", e.Message);
                return null;
            }
        }
    }
}
