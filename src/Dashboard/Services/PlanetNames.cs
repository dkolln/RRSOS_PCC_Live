using System.Text.RegularExpressions;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Reads <c>planets.json</c> (plugin 0.10.3), which maps a planet's hash to its own id ("Prime", "Humble", ...): the
    /// only place that name is ever written down, since a save or a world file only ever carries the hash. Grows as the
    /// owner visits more planets; a hash it has not seen yet just has no name. Cheap to call often: re-read only when
    /// the file's write time or length changes, like <see cref="FactoryService.Book"/>.
    /// </summary>
    public sealed class PlanetNames
    {
        private static readonly Regex Entry = new("\"(-?\\d+)\":\"([^\"]*)\"", RegexOptions.Compiled);

        private readonly string _path;
        private readonly object _lock = new();
        private DateTime _writeUtc;
        private long _length = -1;
        private Dictionary<int, string> _names = new();

        public PlanetNames(IConfiguration config) => _path = LivePaths.Planets(config);

        /// <summary>The planet's own id ("Prime"), or null when this hash has not been learned yet.</summary>
        public string? NameOf(int planetHash) => planetHash != 0 && Names().TryGetValue(planetHash, out var name) ? name : null;

        /// <summary>The name if known, else the hash itself, for when something must be shown.</summary>
        public string Label(int planetHash) => NameOf(planetHash) ?? (planetHash == 0 ? "" : planetHash.ToString());

        private Dictionary<int, string> Names()
        {
            try
            {
                var info = new FileInfo(_path);
                if (!info.Exists)
                    return _names;

                lock (_lock)
                {
                    if (info.LastWriteTimeUtc == _writeUtc && info.Length == _length)
                        return _names;

                    using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();

                    var names = new Dictionary<int, string>();
                    foreach (Match m in Entry.Matches(text))
                    {
                        if (int.TryParse(m.Groups[1].Value, out var hash))
                            names[hash] = m.Groups[2].Value;
                    }

                    _names = names;
                    _writeUtc = info.LastWriteTimeUtc;
                    _length = info.Length;
                    return _names;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return _names;
            }
        }
    }
}
