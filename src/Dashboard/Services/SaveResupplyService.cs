using System.Text;

namespace RRSOS.PCC.Dashboard
{
    public sealed record ResupplyReport(
        bool Success,
        string? Error,
        IReadOnlyList<ResupplyLine> Lines,
        string? BackupPath,
        DateTime At);

    /// <summary>
    /// The Cheats page's RESUPPLY button: applies every configured (container label, product) pair, then every container labelled with an item id, to the
    /// selected save, by editing the save file on disk (see <see cref="SaveResupplyEngine"/>). Generalized from
    /// RRSOS-PCC's ResupplyService, same atomic write with a backup and a before/after signature check.
    ///
    /// The game keeps its world in memory and rewrites the file itself, so this only sticks while the game is at
    /// its main menu or closed. Every run first stores a byte-exact backup of the save, and nothing is written if
    /// the file changes while this is working.
    /// </summary>
    public sealed class SaveResupplyService
    {
        // Old backups are pruned so a busy session does not fill the disk.
        private const int BackupsToKeep = 20;

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly IConfiguration _config;
        private readonly ItemCatalog _catalog;

        public SaveResupplyService(IConfiguration config, ItemCatalog catalog)
        {
            _config = config;
            _catalog = catalog;
        }

        public async Task<ResupplyReport> ResupplyAsync(string savePath, IReadOnlyList<ResupplyConfig> configs, ResupplyOptions? options = null)
        {
            await _gate.WaitAsync();
            try
            {
                return await Task.Run(() => Run(savePath, configs, options));
            }
            finally
            {
                _gate.Release();
            }
        }

        private ResupplyReport Run(string savePath, IReadOnlyList<ResupplyConfig> configs, ResupplyOptions? options)
        {
            var at = DateTime.Now;

            if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath))
                return Fail("No save file is selected.", at);

            try
            {
                var before = Signature(savePath);
                var original = ReadShared(savePath);

                var hasBom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
                var text = Encoding.UTF8.GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0));

                var outcome = options is { AutoFillByGId: true }
                    ? SaveResupplyEngine.Apply(text, configs, autoItem: _catalog.ResolveItemLabel, autoReplaceAll: options.AutoFillReplaceAll)
                    : SaveResupplyEngine.Apply(text, configs);

                if (outcome.Failed)
                    return new ResupplyReport(false, "Nothing was changed. " + string.Join(" ", outcome.Problems), outcome.Lines, null, at);

                if (!outcome.Changed)
                    return new ResupplyReport(true, null, outcome.Lines, null, at);

                // Keep the original before anything is written.
                var backup = WriteBackup(savePath, original, at);

                var edited = (hasBom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>())
                    .Concat(Encoding.UTF8.GetBytes(outcome.NewText!))
                    .ToArray();

                // Write beside the save and swap it in, so a crash can never leave half a save behind.
                var temp = savePath + ".resupply.tmp";
                File.WriteAllBytes(temp, edited);

                try
                {
                    if (Signature(savePath) != before)
                        return new ResupplyReport(false, "The save changed while resupplying, so nothing was written. Is a game still running a world? Try again from the main menu.", outcome.Lines, backup, at);

                    File.Move(temp, savePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }

                PruneBackups(savePath);
                return new ResupplyReport(true, null, outcome.Lines, backup, at);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail("Could not update the save file: " + ex.Message, at);
            }
        }

        private static ResupplyReport Fail(string error, DateTime at) =>
            new(false, error, Array.Empty<ResupplyLine>(), null, at);

        private static (long Length, DateTime LastWriteUtc) Signature(string path)
        {
            var info = new FileInfo(path);
            return (info.Length, info.LastWriteTimeUtc);
        }

        /// <summary>Reads while the game may still have the file open.</summary>
        private static byte[] ReadShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private string BackupFolder => Path.Combine(LivePaths.Folder(_config), "save-backups");

        private string WriteBackup(string savePath, byte[] original, DateTime at)
        {
            Directory.CreateDirectory(BackupFolder);
            var name = Path.GetFileNameWithoutExtension(savePath);
            var backup = Path.Combine(BackupFolder, $"{name}.{at:yyyyMMdd-HHmmss}.json");
            File.WriteAllBytes(backup, original);
            return backup;
        }

        private void PruneBackups(string savePath)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(savePath);
                var old = new DirectoryInfo(BackupFolder)
                    .EnumerateFiles($"{name}.*.json")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(BackupsToKeep);

                foreach (var file in old)
                    file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leaving an old backup behind is harmless.
            }
        }
    }
}
