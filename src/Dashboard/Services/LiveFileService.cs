using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    public enum LiveStatus
    {
        /// <summary>The plugin has never written a file (the game was never started with it).</summary>
        NoFile,

        /// <summary>A file exists but it is old: the game is closed or has stopped.</summary>
        Stale,

        /// <summary>The game is running but on the main menu.</summary>
        Menu,

        /// <summary>In a world and the file is fresh.</summary>
        Live
    }

    /// <summary>
    /// Watches the plugin's live file and keeps the latest reading. It works whether the game or this app
    /// starts first: with no fresh file it simply reports that it is waiting.
    /// </summary>
    public sealed class LiveFileService : BackgroundService
    {
        // The plugin writes once a second, so this many seconds without an update means the game has gone.
        private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(500);

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new TolerantDoubleConverter(), new TolerantNullableDoubleConverter(), new TolerantIntConverter() }
        };

        private readonly ILogger<LiveFileService> _log;
        private readonly PCLauncherService _launcher;
        private readonly string _path;
        private DateTime _lastWriteUtc = DateTime.MinValue;
        private long _lastLength = -1;

        public LiveFileService(ILogger<LiveFileService> log, IConfiguration config, PCLauncherService launcher)
        {
            _log = log;
            _launcher = launcher;
            _path = config["LiveFile"]
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RRSOS-PCC-Live", "live.json");
        }

        public LiveData? Data { get; private set; }

        public LiveStatus Status { get; private set; } = LiveStatus.NoFile;

        /// <summary>Raised when the reading or the status changes. Handlers must hop onto their own thread.</summary>
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
                    _log.LogWarning(e, "Could not read the live file");
                }

                await Task.Delay(PollEvery, stoppingToken);
            }
        }

        private void Poll()
        {
            var changed = false;
            var info = new FileInfo(_path);

            if (!info.Exists)
            {
                changed |= SetStatus(LiveStatus.NoFile);
                if (Data is not null)
                {
                    Data = null;
                    changed = true;
                }
            }
            else
            {
                // The plugin swaps the file in atomically, but be polite about sharing anyway.
                if (info.LastWriteTimeUtc != _lastWriteUtc || info.Length != _lastLength)
                {
                    var parsed = Read();
                    if (parsed is not null)
                    {
                        Data = parsed;
                        _lastWriteUtc = info.LastWriteTimeUtc;
                        _lastLength = info.Length;
                        changed = true;
                    }
                }

                changed |= SetStatus(Classify(Data, _launcher.IsRunning()));
            }

            if (changed)
                Changed?.Invoke();
        }

        private LiveData? Read()
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<LiveData>(stream, Options);
            }
            catch (IOException)
            {
                return null; // being replaced right now; the next poll gets it
            }
            catch (JsonException e)
            {
                _log.LogWarning("The live file is not valid JSON: {Message}", e.Message);
                return null;
            }
        }

        private static LiveStatus Classify(LiveData? data, bool gameRunning)
        {
            if (data?.UpdatedAt is not { } updated)
                return LiveStatus.Stale;

            var fresh = DateTime.UtcNow - updated.ToUniversalTime() <= StaleAfter;

            if (fresh)
                return data.InWorld ? LiveStatus.Live : LiveStatus.Menu;

            // On the main menu the plugin writes "not in a world" once and then stays quiet, so an old file is
            // normal there. Whether the game is still running tells the two cases apart.
            return !data.InWorld && gameRunning ? LiveStatus.Menu : LiveStatus.Stale;
        }

        private bool SetStatus(LiveStatus status)
        {
            if (status == Status)
                return false;

            Status = status;
            return true;
        }
    }
}
