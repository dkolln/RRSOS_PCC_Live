using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Persists the Cheats page's Resupply configs and which save file they apply to, beside the plugin's own
    /// files (see LivePaths). Every config's container label must be unique: two configs naming the same
    /// container would leave it ambiguous which product wins, so a save with a duplicate is refused outright
    /// (the page itself is expected to catch this before ever calling SaveAsync).
    /// </summary>
    public sealed class ResupplyConfigStore
    {
        private readonly string _path;
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        public ResupplyConfigStore(IConfiguration config) =>
            _path = Path.Combine(LivePaths.Folder(config), "resupply-configs.json");

        public async Task<ResupplyConfigFile> LoadAsync()
        {
            if (!File.Exists(_path))
                return new ResupplyConfigFile();

            try
            {
                var text = await File.ReadAllTextAsync(_path);
                return JsonSerializer.Deserialize<ResupplyConfigFile>(text, _json) ?? new ResupplyConfigFile();
            }
            catch (JsonException)
            {
                return new ResupplyConfigFile();
            }
        }

        public async Task SaveAsync(ResupplyConfigFile file)
        {
            var duplicate = file.Configs
                .Where(c => !string.IsNullOrWhiteSpace(c.ContainerLabel))
                .GroupBy(c => c.ContainerLabel.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate != null)
                throw new InvalidOperationException(
                    $"Two configs are both labelled \"{duplicate.Key}\". Container labels must be unique.");

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(file, _json));
        }
    }
}
