using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// What a kind of producer should supply when "Fix" is pressed on the Drone Network tab, kept in <c>producer-supply-defaults.json</c> beside the other
    /// dashboard files, as a producer's group id and the item ids it supplies: <c>{ "Beehive2": ["honey", "Bee1Larvae"] }</c>. A beehive holds only what it
    /// has made so far, so what it holds is no guide to what it makes. The file is written once with the known cases, and is the owner's to edit after that.
    /// A producer with no entry falls back to everything its kind is seen holding.
    /// </summary>
    public sealed class ProducerDefaults
    {
        private static readonly Dictionary<string, string[]> Seed = new(StringComparer.Ordinal)
        {
            // A Beehive2 makes honey and bee larvae (seen in the owner's save, 2026-10-02).
            ["Beehive2"] = new[] { "honey", "Bee1Larvae" }
        };

        private readonly ILogger<ProducerDefaults> _log;
        private readonly string _path;
        private readonly object _lock = new();
        private DateTime _readUtc;
        private Dictionary<string, string[]> _defaults = new(Seed, StringComparer.Ordinal);

        public ProducerDefaults(ILogger<ProducerDefaults> log, IConfiguration config)
        {
            _log = log;
            _path = Path.Combine(LivePaths.Folder(config), "producer-supply-defaults.json");
        }

        /// <summary>The item ids a producer of this group id should supply, or null when none are set for it.</summary>
        public IReadOnlyList<string>? For(string groupId)
        {
            lock (_lock)
            {
                Refresh();
                return _defaults.TryGetValue(groupId, out var items) && items.Length > 0 ? items : null;
            }
        }

        // Called with the lock held: writes the seed when there is no file, and reads the file again when it has changed.
        private void Refresh()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.WriteAllText(_path, JsonSerializer.Serialize(Seed, new JsonSerializerOptions { WriteIndented = true }));
                }

                var written = File.GetLastWriteTimeUtc(_path);
                if (written == _readUtc)
                    return;

                var read = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(_path));
                if (read is not null)
                    _defaults = new Dictionary<string, string[]>(read, StringComparer.Ordinal);

                _readUtc = written;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _log.LogWarning(e, "Could not read {Path}; using the built-in producer defaults", _path);
                _readUtc = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
            }
        }
    }
}
