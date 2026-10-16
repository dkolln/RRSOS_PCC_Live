using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Small switches of the dashboard itself, kept in <c>dashboard-settings.json</c> beside its other files. Remembered between runs.
    /// </summary>
    public sealed class DashboardSettings
    {
        private sealed class Stored
        {
            public bool HideFullContainers { get; set; }
        }

        private readonly ILogger<DashboardSettings> _log;
        private readonly string _path;
        private readonly object _lock = new();

        public DashboardSettings(ILogger<DashboardSettings> log, IConfiguration config)
        {
            _log = log;
            _path = Path.Combine(LivePaths.Folder(config), "dashboard-settings.json");

            try
            {
                if (File.Exists(_path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path), LiveJson.Options) is { } stored)
                {
                    HideFullContainers = stored.HideFullContainers;
                }
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _log.LogWarning(e, "Could not read {Path}; using the defaults", _path);
            }
        }

        /// <summary>True when the Base card's Stored list leaves out the Container3 that are full of one item (a warehouse's stocked chests), so what else is stored shows.</summary>
        public bool HideFullContainers { get; private set; }

        /// <summary>Raised after a setting changes. Handlers must hop onto their own thread.</summary>
        public event Action? Changed;

        public void SetHideFullContainers(bool on) => Set(() =>
        {
            if (HideFullContainers == on)
                return false;

            HideFullContainers = on;
            return true;
        });

        // Applies a change (returning whether it changed anything), keeps all the settings in the file, and tells the listeners.
        private void Set(Func<bool> change)
        {
            lock (_lock)
            {
                if (!change())
                    return;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.WriteAllText(_path, JsonSerializer.Serialize(new Stored { HideFullContainers = HideFullContainers }, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _log.LogWarning(e, "Could not save {Path}", _path);
                }
            }

            Changed?.Invoke();
        }
    }
}
