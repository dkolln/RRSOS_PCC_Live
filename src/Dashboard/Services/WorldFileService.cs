using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Watches the plugin's world file (bases, containers, extractors: it changes every few seconds, not every second)
    /// and keeps the latest reading, with the bases already worked out from it. Like <see cref="LiveFileService"/>, it
    /// does not care whether the game or this app started first.
    /// </summary>
    public sealed class WorldFileService : BackgroundService
    {
        private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(1000);

        private readonly ILogger<WorldFileService> _log;
        private readonly BaseNames _names;
        private readonly ItemCatalog _catalog;
        private readonly string _path;
        private DateTime _lastWriteUtc = DateTime.MinValue;
        private long _lastLength = -1;

        public WorldFileService(ILogger<WorldFileService> log, IConfiguration config, BaseNames names, ItemCatalog catalog)
        {
            _log = log;
            _names = names;
            _catalog = catalog;
            _path = LivePaths.WorldFile(config);
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

            // On the main menu the plugin says "not in a world" and nothing else: that is no bases, not a failure.
            Bases = parsed.InWorld ? BaseDirectory.Build(parsed, _names, _catalog) : BaseDirectory.Empty;
            Data = parsed;
            Changed?.Invoke();
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
