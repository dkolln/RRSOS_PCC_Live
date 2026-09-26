using System.Text;

namespace RRSOS.PCC.Dashboard
{
    public sealed record ResupplyReport(
        bool Success,
        string? Error,
        IReadOnlyList<ResupplyLine> Lines,
        string? BackupPath,
        DateTime At);

    /// <summary>The result of an edit made through <see cref="SaveResupplyService.EditAsync{T}"/>.</summary>
    public sealed record SaveEdit<T>(bool Success, string? Error, T? Result, string? BackupPath, bool Written);

    /// <summary>What Set Supply Lines did, or for a preview would do. Outcome is null when the save could not be read.</summary>
    public sealed record DroneNetworkReport(bool Success, string? Error, DroneNetworkOutcome? Outcome, string? BackupPath, DateTime At);

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

                var (backup, error) = Commit(savePath, original, hasBom, outcome.NewText!, before, at, "resupplying");
                return new ResupplyReport(error is null, error, outcome.Lines, backup, at);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail("Could not update the save file: " + ex.Message, at);
            }
        }

        /// <summary>
        /// Runs an edit on a save's text with the same safety as every Cheats write: the game must not be in a world (the caller
        /// checks), a byte-exact backup is kept first, the new file is written beside the save and swapped in, and nothing is
        /// written if the file changed while working. <paramref name="edit"/> returns the new text (null for no change), a result to
        /// hand back, and an error (which means no write).
        /// </summary>
        public async Task<SaveEdit<T>> EditAsync<T>(string savePath, Func<string, (string? NewText, T Result, string? Error)> edit, string doing)
        {
            await _gate.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    var at = DateTime.Now;
                    if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath))
                        return new SaveEdit<T>(false, "No save file is selected.", default, null, false);

                    try
                    {
                        var before = Signature(savePath);
                        var original = ReadShared(savePath);
                        var hasBom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
                        var text = Encoding.UTF8.GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0));

                        var (newText, result, error) = edit(text);
                        if (error is not null)
                            return new SaveEdit<T>(false, error, result, null, false);

                        if (newText is null)
                            return new SaveEdit<T>(true, null, result, null, false);

                        var (backup, commitError) = Commit(savePath, original, hasBom, newText, before, at, doing);
                        return new SaveEdit<T>(commitError is null, commitError, result, backup, commitError is null);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        return new SaveEdit<T>(false, "Could not update the save file: " + ex.Message, default, null, false);
                    }
                });
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Previews (write: false) or applies the Drone Network settings to the save, changing only the producers and containers named in the wishes; see <see cref="DroneNetworkEngine"/>.</summary>
        public async Task<DroneNetworkReport> DroneNetworkAsync(string savePath, bool write, IReadOnlyDictionary<long, DroneWish>? producerWishes = null, IReadOnlyDictionary<long, DroneWish>? containerWishes = null)
        {
            await _gate.WaitAsync();
            try
            {
                return await Task.Run(() => RunDroneNetwork(savePath, write, producerWishes, containerWishes));
            }
            finally
            {
                _gate.Release();
            }
        }

        private DroneNetworkReport RunDroneNetwork(string savePath, bool write, IReadOnlyDictionary<long, DroneWish>? producerWishes, IReadOnlyDictionary<long, DroneWish>? containerWishes)
        {
            var at = DateTime.Now;

            if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath))
                return new DroneNetworkReport(false, "No save file is selected.", null, null, at);

            try
            {
                var before = Signature(savePath);
                var original = ReadShared(savePath);

                var hasBom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
                var text = Encoding.UTF8.GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0));

                var outcome = DroneNetworkEngine.Apply(text, label => _catalog.ResolveLabel(label), producerWishes, containerWishes);

                if (outcome.Failed)
                    return new DroneNetworkReport(false, "Nothing was changed. " + string.Join(" ", outcome.Problems), outcome, null, at);

                if (!write || !outcome.Changed)
                    return new DroneNetworkReport(true, null, outcome, null, at);

                var (backup, error) = Commit(savePath, original, hasBom, outcome.NewText!, before, at, "setting the drone network");
                return new DroneNetworkReport(error is null, error, outcome, backup, at);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new DroneNetworkReport(false, "Could not read or update the save file: " + ex.Message, null, null, at);
            }
        }

        /// <summary>
        /// Backs the original up, then writes the edited text beside the save and swaps it in, so a crash can never leave
        /// half a save behind. Nothing is written if the file changed since it was read.
        /// </summary>
        private (string? Backup, string? Error) Commit(string savePath, byte[] original, bool hasBom, string newText, (long Length, DateTime LastWriteUtc) before, DateTime at, string doing)
        {
            // Keep the original before anything is written.
            var backup = WriteBackup(savePath, original, at);

            var edited = (hasBom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>())
                .Concat(Encoding.UTF8.GetBytes(newText))
                .ToArray();

            var temp = savePath + ".resupply.tmp";
            File.WriteAllBytes(temp, edited);

            try
            {
                if (Signature(savePath) != before)
                    return (backup, $"The save changed while {doing}, so nothing was written. Is a game still running a world? Try again from the main menu.");

                File.Move(temp, savePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            PruneBackups(savePath);
            return (backup, null);
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
