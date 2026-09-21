using System.Text.Json;
using System.Text.Json.Serialization;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Names the bases. A sign standing at the base wins; otherwise the name saved for it; otherwise the next unused name
    /// from a procedural pool (Greek names for bases, NATO letters for outposts), which is then saved so it stays put.
    /// The names live in this app's own file (basedata.json beside the live file), never in RRSOS-PCC's: the two
    /// apps do not depend on each other. They are keyed by the game's own id for the pod, so they survive restarts.
    /// </summary>
    public sealed class BaseNames
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private sealed class Entry
        {
            [JsonPropertyName("name")] public string Name { get; set; } = "";

            /// <summary>True when the name came from a sign (kept even if the sign goes), false when it was handed out from the pool.</summary>
            [JsonPropertyName("manual")] public bool Manual { get; set; }

            [JsonPropertyName("type")] public string Type { get; set; } = "";
        }

        private readonly object _lock = new();
        private readonly string _path;
        private readonly ILogger<BaseNames> _log;
        private readonly List<string> _basePool;
        private readonly List<string> _outpostPool;
        private readonly Dictionary<string, Entry> _entries;
        private bool _dirty;

        public BaseNames(IConfiguration config, IWebHostEnvironment env, ILogger<BaseNames> log)
        {
            _log = log;
            _path = LivePaths.BaseData(config);

            var assets = Path.Combine(env.ContentRootPath, "Assets");
            _basePool = ReadList(Path.Combine(assets, "basenames.json"));
            _outpostPool = ReadList(Path.Combine(assets, "outpostnames.json"));
            _entries = ReadEntries();
        }

        /// <summary>The name for the base with this id (the game's id for its pod). Sign text, if any, is what the sign says.</summary>
        public string Resolve(long id, BaseKind kind, string? signText)
        {
            lock (_lock)
            {
                var key = id.ToString();

                if (!string.IsNullOrWhiteSpace(signText))
                {
                    if (!_entries.TryGetValue(key, out var known) || known.Name != signText || !known.Manual)
                    {
                        _entries[key] = new Entry { Name = signText, Manual = true, Type = kind.ToString() };
                        _dirty = true;
                    }

                    return signText;
                }

                if (_entries.TryGetValue(key, out var saved))
                    return saved.Name;

                var name = NextFromPool(id, kind);
                _entries[key] = new Entry { Name = name, Manual = false, Type = kind.ToString() };
                _dirty = true;
                return name;
            }
        }

        /// <summary>Writes the names down if any changed since the last time.</summary>
        public void FlushIfDirty()
        {
            lock (_lock)
            {
                if (!_dirty)
                    return;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    var temp = _path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(_entries, WriteOptions));
                    File.Move(temp, _path, overwrite: true);
                    _dirty = false;
                }
                catch (Exception e)
                {
                    // Names still work for this run; try again next time.
                    _log.LogWarning(e, "Could not save the base names to {Path}", _path);
                }
            }
        }

        private string NextFromPool(long id, BaseKind kind)
        {
            var taken = new HashSet<string>(_entries.Values.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            var unused = (kind == BaseKind.Base ? _basePool : _outpostPool).FirstOrDefault(name => !taken.Contains(name));
            if (unused is not null)
                return unused;

            // Pool used up: the last four digits of the id.
            var digits = id.ToString();
            return digits.Length > 4 ? digits[^4..] : digits;
        }

        private static List<string> ReadList(string path)
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new();
            }
            catch (Exception)
            {
                return new();
            }
        }

        private Dictionary<string, Entry> ReadEntries()
        {
            if (!File.Exists(_path))
                return new(StringComparer.OrdinalIgnoreCase);

            try
            {
                var read = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path)) ?? new();
                return new(read, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                // Never let a later save silently overwrite the user's names.
                _log.LogWarning(e, "{Path} is not readable; keeping a copy and starting fresh", _path);
                try { File.Copy(_path, _path + ".corrupt", overwrite: true); } catch (Exception) { }
                return new(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
