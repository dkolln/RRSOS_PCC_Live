using System.Collections.Concurrent;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The Cheats page's Storage tab: reads the storage units of a save (see <see cref="StorageTransferEngine"/>) and moves or deletes items in them, with the same
    /// backup-first, verified write as every other Cheats edit (<see cref="SaveResupplyService.EditAsync{T}"/>). It is an offline edit: the game must not be in a world
    /// (the caller checks).
    /// <para>
    /// A save is large (several MB, tens of thousands of records), so what was read is kept, tagged with the file's size and write time, and handed back for as long
    /// as the file on disk is exactly that. <see cref="StorageWarmupService"/> reads the newest saves ahead of time whenever the game is not in a world, so the tab opens
    /// at once; a save that changed (the game saved again, or an edit was written) is simply read again.
    /// </para>
    /// </summary>
    public sealed class StorageService
    {
        private readonly SaveResupplyService _saves;
        private readonly ConcurrentDictionary<string, (long Length, DateTime WrittenUtc, StorageSnapshot Snapshot)> _cache = new(StringComparer.OrdinalIgnoreCase);

        public StorageService(SaveResupplyService saves) => _saves = saves;

        /// <summary>Every storage unit in the save (all planets). Read only: nothing is written. Served from what was read before when the file has not changed since.</summary>
        public Task<StorageSnapshot> LoadAsync(string savePath) => Task.Run(() =>
        {
            if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath))
                return StorageSnapshot.Empty();

            // The file's size and time are taken before it is read: if it changes while being read, the next look sees a different signature and reads it again.
            var info = new FileInfo(savePath);
            if (_cache.TryGetValue(savePath, out var hit) && hit.Length == info.Length && hit.WrittenUtc == info.LastWriteTimeUtc)
                return hit.Snapshot;

            var text = BaseBuildingService.ReadText(savePath, out var error);
            if (text is null)
                return StorageSnapshot.Empty(error ?? "The save could not be read.");

            var snapshot = StorageTransferEngine.Read(text);
            if (snapshot.Error is null)
                _cache[savePath] = (info.Length, info.LastWriteTimeUtc, snapshot);

            return snapshot;
        });

        /// <summary>Reads the save now if it is not already held, so the tab opens without waiting.</summary>
        public Task WarmAsync(string savePath) => LoadAsync(savePath);

        /// <summary>Writes the plan into the save, after a backup. Nothing is written if the plan does not check out against the save as it is now, or reaches off the given planet.</summary>
        public async Task<SaveEdit<StorageOutcome>> ApplyAsync(string savePath, StoragePlan plan, int planetHash)
        {
            var edit = await _saves.EditAsync<StorageOutcome>(savePath, text =>
            {
                var outcome = StorageTransferEngine.Apply(text, plan, planetHash);
                return (outcome.NewText, outcome, outcome.Failed ? string.Join(" ", outcome.Problems) : null);
            }, "moving items between storage");

            if (edit.Written)
                _cache.TryRemove(savePath, out _);

            return edit;
        }
    }
}
