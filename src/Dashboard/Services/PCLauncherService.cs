using System.Diagnostics;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Starts The Planet Crafter through Steam and says whether it is running. Both can be changed in settings
    /// (<c>Game:LaunchUri</c>, <c>Game:ProcessName</c>), for a copy of the game that is not started through Steam.
    /// </summary>
    public sealed class PCLauncherService
    {
        private readonly string _processName;
        private readonly string _launchUri;

        // Scanning every process is not free; the page asks often.
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(2);

        private readonly object _lock = new();
        private DateTime _checkedAtUtc = DateTime.MinValue;
        private bool _running;

        public PCLauncherService(IConfiguration config)
        {
            _processName = LivePaths.Setting(config, "Game:ProcessName") ?? "Planet Crafter";
            _launchUri = LivePaths.Setting(config, "Game:LaunchUri") ?? "steam://rungameid/1284190";
        }

        public bool IsRunning()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _checkedAtUtc < CacheFor)
                    return _running;

                var processes = Process.GetProcessesByName(_processName);
                try
                {
                    _running = processes.Length > 0;
                }
                finally
                {
                    foreach (var p in processes)
                        p.Dispose();
                }

                _checkedAtUtc = DateTime.UtcNow;
                return _running;
            }
        }

        /// <summary>Starts the game unless it is already running. It never closes or restarts a running game.</summary>
        public void Launch()
        {
            if (IsRunning())
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = _launchUri,
                UseShellExecute = true
            });

            lock (_lock)
                _checkedAtUtc = DateTime.MinValue;
        }
    }
}
