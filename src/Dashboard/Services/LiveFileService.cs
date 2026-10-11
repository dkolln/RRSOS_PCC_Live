using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    public enum LiveStatus
    {
        /// <summary>The plugin has never written a file (the game was never started with it).</summary>
        NoFile,

        /// <summary>A file exists but it is old: the game is closed or has stopped.</summary>
        Stale,

        /// <summary>The game is running but on the main menu.</summary>
        Menu,

        /// <summary>In a world and the file is fresh.</summary>
        Live
    }

    /// <summary>
    /// Watches the plugin's live file and keeps the latest reading. It works whether the game or this app
    /// starts first: with no fresh file it simply reports that it is waiting.
    /// </summary>
    public sealed class LiveFileService : BackgroundService
    {
        // The plugin writes once a second, so this many seconds without an update means the game has gone.
        private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(500);

        /// <summary>How loud a phase announcement speaks, independent of the Home page's own volume slider: this fires from
        /// the background, whether or not a browser tab is even open (see <see cref="WorldFileService"/>'s rocket alerts,
        /// the same idea).</summary>
        private const double PhaseAlertVolume = 0.9;
        private static readonly TimeSpan PhaseAlertCooldown = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan PhaseSettleTime = TimeSpan.FromSeconds(20);

        private readonly ILogger<LiveFileService> _log;
        private readonly PCLauncherService _launcher;
        private readonly SpeechService _speech;
        private readonly string _path;
        private DateTime _lastWriteUtc = DateTime.MinValue;
        private long _lastLength = -1;

        // Whether a planet's phase (keyed "<planetId>:<phaseId>") was complete last time this was read, and which phase
        // (by id) was the in-progress one for a planet, so a change can be told apart from "first time we've looked".
        private readonly Dictionary<string, bool> _phaseComplete = new();
        private readonly Dictionary<string, string?> _phaseInProgress = new();

        // The planet the phase memory above belongs to (null at the menu) and when it stops just re-learning after a load.
        private string? _phaseWorldId;
        private DateTime _phaseSettledAt = DateTime.MinValue;

        public LiveFileService(ILogger<LiveFileService> log, IConfiguration config, PCLauncherService launcher, SpeechService speech)
        {
            _log = log;
            _launcher = launcher;
            _speech = speech;
            _path = LivePaths.LiveFile(config);
        }

        public LiveData? Data { get; private set; }

        public LiveStatus Status { get; private set; } = LiveStatus.NoFile;

        /// <summary>Raised when the reading or the status changes. Handlers must hop onto their own thread.</summary>
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
                    _log.LogWarning(e, "Could not read the live file");
                }

                await Task.Delay(PollEvery, stoppingToken);
            }
        }

        private void Poll()
        {
            var changed = false;
            var info = new FileInfo(_path);

            if (!info.Exists)
            {
                changed |= SetStatus(LiveStatus.NoFile);
                if (Data is not null)
                {
                    Data = null;
                    changed = true;
                }
            }
            else
            {
                // The plugin swaps the file in atomically, but be polite about sharing anyway.
                if (info.LastWriteTimeUtc != _lastWriteUtc || info.Length != _lastLength)
                {
                    var parsed = Read();
                    if (parsed is not null)
                    {
                        AnnouncePhases(parsed);
                        Data = parsed;
                        _lastWriteUtc = info.LastWriteTimeUtc;
                        _lastLength = info.Length;
                        changed = true;
                    }
                }

                changed |= SetStatus(Classify(Data, _launcher.IsRunning()));
            }

            if (changed)
                Changed?.Invoke();
        }

        /// <summary>
        /// Speaks as the current planet's terraformation phases complete, and as the next one becomes the one in progress
        /// (see <see cref="PhaseData"/>): "Phase X is complete", "Phase Y now in progress". When the very last phase
        /// finishes, that is the one exception: just "Terraformation is complete congratulations planet crafter", not
        /// also that phase's own "is complete" (the owner asked for one sentence there, not two). Each planet's phases are
        /// kept apart by planet id, so switching planets never looks like a phase un-completing. Nothing is spoken for the
        /// first <see cref="PhaseSettleTime"/> after a world loads (or the planet changes): that stretch only learns where
        /// things stand, so phases already finished are never announced again. A planet whose phases are not (yet) in the
        /// file is simply left alone.
        /// </summary>
        private void AnnouncePhases(LiveData data)
        {
            if (!data.InWorld)
            {
                // Back at the menu: whatever loads next is a different session (maybe another save), so what was remembered
                // here must not be compared against it.
                _phaseComplete.Clear();
                _phaseInProgress.Clear();
                _phaseWorldId = null;
                return;
            }

            if (data.Planet?.Phases is not { Count: > 0 } phases)
                return;

            var planetId = data.PlanetId ?? "";
            var now = DateTime.UtcNow;

            // A freshly loaded world's first readings can have the units still filling in (Terraformation briefly reads low,
            // then jumps to the real value), which looks exactly like every finished phase completing just now. So after a
            // load, a planet change, or this service starting, only re-learn where things stand until it has settled.
            if (_phaseWorldId != planetId)
            {
                _phaseWorldId = planetId;
                _phaseSettledAt = now + PhaseSettleTime;
                _phaseComplete.Clear();
                _phaseInProgress.Clear();
            }

            var settling = now < _phaseSettledAt;
            string? firstIncomplete = null;
            var newlyDone = new List<PhaseData>();

            foreach (var phase in phases)
            {
                if (string.IsNullOrEmpty(phase.Id))
                    continue;

                var key = planetId + ":" + phase.Id;
                if (!settling && _phaseComplete.TryGetValue(key, out var was) && !was && phase.Complete)
                    newlyDone.Add(phase);

                _phaseComplete[key] = phase.Complete;

                if (firstIncomplete is null && !phase.Complete)
                    firstIncomplete = phase.Id;
            }

            var hasPrior = _phaseInProgress.TryGetValue(planetId, out var wasInProgress) && !settling;
            var allDone = hasPrior && wasInProgress is not null && firstIncomplete is null;

            if (allDone)
            {
                _speech.Alert("phase-all-done:" + planetId, "Terraformation is complete congratulations planet crafter.", PhaseAlertVolume, PhaseAlertCooldown);
            }
            else
            {
                foreach (var phase in newlyDone)
                    _speech.Alert("phase-done:" + planetId + ":" + phase.Id, $"Phase {phase.Name} is complete.", PhaseAlertVolume, PhaseAlertCooldown);

                if (hasPrior && wasInProgress != firstIncomplete && firstIncomplete is not null)
                {
                    var name = phases.FirstOrDefault(p => p.Id == firstIncomplete)?.Name ?? firstIncomplete;
                    _speech.Alert("phase-next:" + planetId + ":" + firstIncomplete, $"Phase {name} now in progress.", PhaseAlertVolume, PhaseAlertCooldown);
                }
            }

            _phaseInProgress[planetId] = firstIncomplete;
        }

        private LiveData? Read()
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<LiveData>(stream, LiveJson.Options);
            }
            catch (IOException)
            {
                return null; // being replaced right now; the next poll gets it
            }
            catch (JsonException e)
            {
                _log.LogWarning("The live file is not valid JSON: {Message}", e.Message);
                return null;
            }
        }

        private static LiveStatus Classify(LiveData? data, bool gameRunning)
        {
            if (data?.UpdatedAt is not { } updated)
                return LiveStatus.Stale;

            var fresh = DateTime.UtcNow - updated.ToUniversalTime() <= StaleAfter;

            if (fresh)
                return data.InWorld ? LiveStatus.Live : LiveStatus.Menu;

            // On the main menu the plugin writes "not in a world" once and then stays quiet, so an old file is
            // normal there. Whether the game is still running tells the two cases apart.
            return !data.InWorld && gameRunning ? LiveStatus.Menu : LiveStatus.Stale;
        }

        private bool SetStatus(LiveStatus status)
        {
            if (status == Status)
                return false;

            Status = status;
            return true;
        }
    }
}
