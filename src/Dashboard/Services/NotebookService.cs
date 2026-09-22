using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    public sealed class NotebookService
    {
        private readonly string _path;
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        public NotebookService(IConfiguration config) =>
            _path = Path.Combine(LivePaths.Folder(config), "notebook.json");

        public async Task<Notebook> LoadAsync()
        {
            if (!File.Exists(_path))
            {
                var empty = new Notebook();
                await SaveAsync(empty);
                return empty;
            }

            var text = await File.ReadAllTextAsync(_path);
            return JsonSerializer.Deserialize<Notebook>(text) ?? new Notebook();
        }

        public async Task SaveAsync(Notebook notebook)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(notebook, _json));
        }
    }
}
