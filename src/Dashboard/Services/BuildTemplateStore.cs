using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The Cheats page's platform templates (what one platform of chests looks like), kept beside the plugin's own files (see
    /// LivePaths) as <c>build-templates.json</c>: one per chest type, named by the owner. Per PC, not in the repo.
    /// </summary>
    public sealed class BuildTemplateStore
    {
        private readonly string _path;
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        private readonly SemaphoreSlim _gate = new(1, 1);

        public BuildTemplateStore(IConfiguration config) =>
            _path = Path.Combine(LivePaths.Folder(config), "build-templates.json");

        public string Path_ => _path;

        public async Task<List<BuildTemplate>> LoadAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (!File.Exists(_path))
                    return new List<BuildTemplate>();

                try
                {
                    return JsonSerializer.Deserialize<List<BuildTemplate>>(await File.ReadAllTextAsync(_path), _json) ?? new List<BuildTemplate>();
                }
                catch (JsonException)
                {
                    return new List<BuildTemplate>();
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Adds a template, or replaces the one with the same name.</summary>
        public async Task SaveAsync(BuildTemplate template)
        {
            var all = await LoadAsync();
            all.RemoveAll(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase));
            all.Add(template);

            await _gate.WaitAsync();
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(all.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(), _json));
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DeleteAsync(string name)
        {
            var all = await LoadAsync();
            if (all.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
                return;

            await _gate.WaitAsync();
            try
            {
                await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(all, _json));
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
