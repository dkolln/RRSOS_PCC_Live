namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Reads the newest save files ahead of the Storage tab (see <see cref="StorageService"/>) whenever the game is not in a world: right after the player goes back to the
    /// main menu, when the game has just written the save, and again if a save changes. While a world is loaded nothing is read, since the game keeps rewriting the
    /// files then and the tab is closed to edits anyway. A save already held, and unchanged, costs only a look at its size and time.
    /// </summary>
    public sealed class StorageWarmupService : BackgroundService
    {
        private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(5);

        /// <summary>How many of the newest saves are kept ready: the one being played and its backup.</summary>
        private const int Newest = 2;

        private readonly ILogger<StorageWarmupService> _log;
        private readonly LiveFileService _live;
        private readonly StorageService _storage;
        private readonly DashboardSettings _settings;
        private readonly string _folder;

        public StorageWarmupService(ILogger<StorageWarmupService> log, LiveFileService live, StorageService storage, DashboardSettings settings, IConfiguration config)
        {
            _log = log;
            _live = live;
            _storage = storage;
            _settings = settings;
            _folder = SaveFolderResolver.DetectFolder(config);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Give the app a moment to finish starting before reading megabytes.
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Live only means no save is read on the dashboard's own account: the Storage tab then reads one when it is opened.
                    if (!_settings.LiveOnly && _live.Status != LiveStatus.Live && Directory.Exists(_folder))
                    {
                        var newest = SaveFolderResolver.ListSaveFiles(_folder)
                            .Select(name => new FileInfo(Path.Combine(_folder, name)))
                            .Where(f => f.Exists)
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .Take(Newest);

                        foreach (var file in newest)
                            await _storage.WarmAsync(file.FullName);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _log.LogWarning(e, "Could not read a save ahead of time");
                }

                await Task.Delay(PollEvery, stoppingToken);
            }
        }
    }
}
