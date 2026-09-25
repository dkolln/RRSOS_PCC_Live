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

        /// <summary>Raised when a note is added from somewhere other than the notes list itself (the object search), so the list reloads.</summary>
        public event Action? Changed;

        /// <summary>Adds a note at the end of the list and saves it.</summary>
        public async Task AddNoteAsync(string text)
        {
            var notebook = await LoadAsync();
            var nextId = notebook.Notes.Count == 0 ? 1 : notebook.Notes.Max(n => n.Id) + 1;
            var nextPriority = notebook.Notes.Count == 0 ? 1 : notebook.Notes.Max(n => n.Priority) + 1;

            notebook.Notes.Add(new Note { Id = nextId, Text = text, Created = DateTime.UtcNow, Priority = nextPriority });
            await SaveAsync(notebook);
            Changed?.Invoke();
        }

        public async Task SaveAsync(Notebook notebook)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(notebook, _json));
        }
    }
}
