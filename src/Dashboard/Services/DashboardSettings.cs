using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Small switches of the dashboard itself, kept in <c>dashboard-settings.json</c> beside its other files. Today one: <see cref="LiveOnly"/>, which turns off everything
    /// that is read from a save file while the game is being played (the boneyard, and reading saves ahead of the Storage tab), so the page only works from what the plugin
    /// reports live. Remembered between runs; off by default.
    /// </summary>
    public sealed class DashboardSettings
    {
        private sealed class Stored
        {
            public bool LiveOnly { get; set; }
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
                    LiveOnly = stored.LiveOnly;
                    HideFullContainers = stored.HideFullContainers;
                }
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _log.LogWarning(e, "Could not read {Path}; using the defaults", _path);
            }
        }

        /// <summary>True when nothing is read from save files while playing: no boneyard, no saves read ahead.</summary>
        public bool LiveOnly { get; private set; }

        /// <summary>True when the Base card's Stored list leaves out the Container3 that are full of one item (a warehouse's stocked chests), so what else is stored shows.</summary>
        public bool HideFullContainers { get; private set; }

        /// <summary>Raised after a setting changes. Handlers must hop onto their own thread.</summary>
        public event Action? Changed;

        public void SetLiveOnly(bool on) => Set(() =>
        {
            if (LiveOnly == on)
                return false;

            LiveOnly = on;
            return true;
        });

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
                    File.WriteAllText(_path, JsonSerializer.Serialize(new Stored { LiveOnly = LiveOnly, HideFullContainers = HideFullContainers }, new JsonSerializerOptions { WriteIndented = true }));
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
