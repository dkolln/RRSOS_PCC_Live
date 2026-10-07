using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The plugin's <c>terraformers.json</c>, parsed (plugin 0.13.0): what makes each planet stat. It is rewritten every few seconds while a world is loaded, so the parsed
    /// copy is kept until the file's size or time changes rather than reading it again on every look.
    /// </summary>
    public sealed class TerraformersService
    {
        private readonly string _path;
        private readonly object _lock = new();
        private DateTime _writtenUtc;
        private long _length = -1;
        private TerraformerFile? _file;

        public TerraformersService(IConfiguration config) => _path = Path.Combine(LivePaths.Folder(config), "terraformers.json");

        /// <summary>The latest reading, or null when the plugin has not written one (an older plugin, or no world loaded yet).</summary>
        public TerraformerFile? Get()
        {
            try
            {
                var info = new FileInfo(_path);
                if (!info.Exists)
                    return null;

                lock (_lock)
                {
                    if (_file is not null && info.LastWriteTimeUtc == _writtenUtc && info.Length == _length)
                        return _file;

                    using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var parsed = JsonSerializer.Deserialize<TerraformerFile>(stream, LiveJson.Options);
                    if (parsed is null)
                        return _file;

                    _file = parsed;
                    _writtenUtc = info.LastWriteTimeUtc;
                    _length = info.Length;
                    return _file;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return _file; // being replaced right now: the last good reading will do until the next look
            }
        }
    }
}
