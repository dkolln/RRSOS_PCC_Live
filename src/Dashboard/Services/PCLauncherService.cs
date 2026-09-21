using System.Diagnostics;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>Starts The Planet Crafter through Steam and says whether it is running.</summary>
    public sealed class PCLauncherService
    {
        private const string ProcessName = "Planet Crafter";
        private const string SteamLaunchUri = "steam://rungameid/1284190";

        // Scanning every process is not free; the page asks often.
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(2);

        private readonly object _lock = new();
        private DateTime _checkedAtUtc = DateTime.MinValue;
        private bool _running;

        public bool IsRunning()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _checkedAtUtc < CacheFor)
                    return _running;

                var processes = Process.GetProcessesByName(ProcessName);
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
                FileName = SteamLaunchUri,
                UseShellExecute = true
            });

            lock (_lock)
                _checkedAtUtc = DateTime.MinValue;
        }
    }
}
