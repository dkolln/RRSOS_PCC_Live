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
                    LiveOnly = stored.LiveOnly;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _log.LogWarning(e, "Could not read {Path}; using the defaults", _path);
            }
        }

        /// <summary>True when nothing is read from save files while playing: no boneyard, no saves read ahead.</summary>
        public bool LiveOnly { get; private set; }

        /// <summary>Raised after a setting changes. Handlers must hop onto their own thread.</summary>
        public event Action? Changed;

        public void SetLiveOnly(bool on)
        {
            lock (_lock)
            {
                if (LiveOnly == on)
                    return;

                LiveOnly = on;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.WriteAllText(_path, JsonSerializer.Serialize(new Stored { LiveOnly = on }, new JsonSerializerOptions { WriteIndented = true }));
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
