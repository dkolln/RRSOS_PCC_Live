using System.Globalization;
using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>One object the save says is out in the world (it has a position), rather than in an inventory.</summary>
    public sealed record SaveObject(string GId, Vec3 Position, int PlanetHash);

    /// <summary>
    /// The boneyard's source: loose items read from the game's newest save, the way RRSOS-PCC does it, instead of live
    /// from the game. Every 10 seconds it looks at the save folder; when the newest save (the game writes the one being
    /// played, plus a copy called Backup) has been written since the last look, it reads every object that has a position
    /// in it. An item in a container, a backpack or a vehicle has none, so what is left is what lies in the world (and the
    /// buildings, which the boneyard's own filter leaves out). So the boneyard is as fresh as the last save, autosave
    /// included. It only reads the save, and never while the game is still writing it.
    /// </summary>
    public sealed class SaveLooseService : BackgroundService
    {
        private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(10);

        /// <summary>A save written less than this long ago may still be being written; it is read on the next look.</summary>
        private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

        private readonly ILogger<SaveLooseService> _log;
        private readonly IConfiguration _config;
        private string? _lastPath;
        private DateTime _lastWriteUtc;
        private long _lastLength = -1;

        public SaveLooseService(ILogger<SaveLooseService> log, IConfiguration config)
        {
            _log = log;
            _config = config;
        }

        /// <summary>Everything with a position in the last save read. Empty until one has been read.</summary>
        public IReadOnlyList<SaveObject> Objects { get; private set; } = Array.Empty<SaveObject>();

        /// <summary>Which save that was ("Custom-1"), and when the game wrote it. Null until one has been read.</summary>
        public string? SaveName { get; private set; }
        public DateTime? SavedAtUtc { get; private set; }

        /// <summary>Raised when a newer save has been read. Handlers must hop onto their own thread.</summary>
        public event Action? Changed;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Poll();
                }
                catch (Exception e)
                {
                    _log.LogWarning(e, "Could not read the save for loose items");
                }

                await Task.Delay(PollEvery, stoppingToken);
            }
        }

        private void Poll()
        {
            var newest = NewestSave(SaveFolderResolver.DetectFolder(_config));
            if (newest is null)
                return;

            if (newest.FullName == _lastPath && newest.LastWriteTimeUtc == _lastWriteUtc && newest.Length == _lastLength)
                return;

            if (DateTime.UtcNow - newest.LastWriteTimeUtc < Settle)
                return;

            var objects = Read(newest.FullName);
            if (objects is null)
                return; // being written or not readable right now; the next look tries again

            _lastPath = newest.FullName;
            _lastWriteUtc = newest.LastWriteTimeUtc;
            _lastLength = newest.Length;

            Objects = objects;
            SaveName = Path.GetFileNameWithoutExtension(newest.Name);
            SavedAtUtc = newest.LastWriteTimeUtc;

            _log.LogInformation("Loose items read from {Save}, saved {At:HH:mm:ss}: {Count} objects with a position", SaveName, SavedAtUtc.Value.ToLocalTime(), objects.Count);
            Changed?.Invoke();
        }

        // The save being played: the newest .json in the folder, leaving out the game's Backup copy of it.
        private static FileInfo? NewestSave(string folder)
        {
            try
            {
                var dir = new DirectoryInfo(folder);
                if (!dir.Exists)
                    return null;

                return dir.EnumerateFiles("*.json")
                    .Where(f => !f.Name.StartsWith("Backup", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Every record with a group id and a position. A save is a run of JSON records separated by "|", in sections
        /// separated by "@"; item records look like {"id":104079455,"gId":"Titanium","pos":"767.26,31.79,-21.41",...,"planet":-1140328421}.
        /// </summary>
        private List<SaveObject>? Read(string path)
        {
            string text;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }
            catch (IOException)
            {
                return null;
            }

            var found = new List<SaveObject>();

            foreach (var part in text.Split('@', '|'))
            {
                var record = part.Trim().TrimStart('﻿');
                if (!record.StartsWith('{') || !record.Contains("\"pos\"") || !record.Contains("\"gId\""))
                    continue;

                try
                {
                    using var json = JsonDocument.Parse(record);
                    var root = json.RootElement;

                    if (root.TryGetProperty("gId", out var gId) && gId.GetString() is { Length: > 0 } id
                        && root.TryGetProperty("pos", out var pos) && Position(pos.GetString()) is { } position)
                    {
                        var planet = root.TryGetProperty("planet", out var p) && p.TryGetInt32(out var hash) ? hash : 0;
                        found.Add(new SaveObject(id, position, planet));
                    }
                }
                catch (JsonException)
                {
                    // One odd record does not spoil the rest.
                }
            }

            return found;
        }

        // "x,y,z" in the save's invariant format; the origin means "not placed".
        private static Vec3? Position(string? text)
        {
            var parts = text?.Split(',');
            if (parts is not { Length: 3 })
                return null;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                return null;

            return x == 0 && y == 0 && z == 0 ? null : new Vec3 { X = x, Y = y, Z = z };
        }
    }
}
