using System.Runtime.Versioning;
using System.Speech.Synthesis;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The spoken alerts, through Windows' own speech engine rather than the browser's. So they use the default voice set
    /// in Windows (Settings > Time &amp; language > Speech), whatever browser shows the page. The sound comes from the PC
    /// running the dashboard. Alerts queue rather than interrupt each other: a newer one waits its turn, with a short
    /// pause after the one before it, so two that land close together (a phase finishing and the next one starting, say)
    /// are both heard in full instead of the second cutting the first off mid-word. Where Windows speech is not
    /// available, alerts are simply silent.
    /// </summary>
    public sealed class SpeechService : IDisposable
    {
        // The browser voice ran at 0.92 of normal speed; the nearest step on Windows' -10 to 10 scale.
        private const int Rate = -1;

        /// <summary>How long to leave between one queued phrase finishing and the next starting.</summary>
        private static readonly TimeSpan PauseBetween = TimeSpan.FromMilliseconds(600);

        private readonly ILogger<SpeechService> _log;
        private readonly object _lock = new();
        private readonly Dictionary<string, DateTime> _lastSpoken = new();
        private readonly Queue<(string Text, double Volume)> _queue = new();
        private SpeechSynthesizer? _synth;
        private bool _unavailable;

        /// <summary>True from the moment something starts speaking until the queue is empty again (through the pauses between).</summary>
        private bool _busy;

        public SpeechService(ILogger<SpeechService> log) => _log = log;

        /// <summary>
        /// Says an alert unless the same one (<paramref name="key"/>) was said less than <paramref name="cooldown"/> ago.
        /// The cooldown is kept here, not per page, so a second open tab does not say every alert again.
        /// </summary>
        public void Alert(string key, string text, double volume, TimeSpan cooldown)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (_lastSpoken.TryGetValue(key, out var last) && now - last < cooldown)
                    return;

                _lastSpoken[key] = now;
            }

            Speak(text, volume);
        }

        /// <summary>Queues <paramref name="text"/> to be said at <paramref name="volume"/> (0 to 1), after whatever is already
        /// queued finishes (see the class summary).</summary>
        public void Speak(string text, double volume)
        {
            if (!OperatingSystem.IsWindows())
                return;

            lock (_lock)
            {
                _queue.Enqueue((text, volume));
                if (!_busy)
                    StartNextLocked();
            }
        }

        /// <summary>Dequeues and speaks the next phrase, if the engine is available. Must be called with <see cref="_lock"/> held.</summary>
        [SupportedOSPlatform("windows")]
        private void StartNextLocked()
        {
            if (_queue.Count == 0)
            {
                _busy = false;
                return;
            }

            var synth = Synth();
            if (synth is null)
            {
                _queue.Clear();
                _busy = false;
                return;
            }

            var (text, volume) = _queue.Dequeue();
            _busy = true;

            try
            {
                synth.Volume = (int)Math.Round(Math.Clamp(volume, 0, 1) * 100);
                synth.SpeakAsync(text);
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "Could not speak an alert");
                _busy = false;
            }
        }

        private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
        {
            bool more;
            lock (_lock)
            {
                more = _queue.Count > 0;
                if (!more)
                    _busy = false;
            }

            if (!more)
                return;

            // Off the lock, and off the synthesizer's own event thread, so the pause does not block either.
            _ = Task.Delay(PauseBetween).ContinueWith(_ =>
            {
                lock (_lock)
                    StartNextLocked();
            });
        }

        [SupportedOSPlatform("windows")]
        private SpeechSynthesizer? Synth()
        {
            if (_synth is not null || _unavailable)
                return _synth;

            try
            {
                // A new synthesizer starts with Windows' default voice and the default audio device.
                _synth = new SpeechSynthesizer { Rate = Rate };
                _synth.SetOutputToDefaultAudioDevice();
                _synth.SpeakCompleted += OnSpeakCompleted;
                _log.LogInformation("Spoken alerts use the Windows voice {Voice}", _synth.Voice.Name);
            }
            catch (Exception e)
            {
                _unavailable = true;
                _log.LogWarning(e, "Windows speech is not available; alerts will be silent");
            }

            return _synth;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _queue.Clear();
                if (OperatingSystem.IsWindows() && _synth is not null)
                {
                    _synth.SpeakCompleted -= OnSpeakCompleted;
                    _synth.Dispose();
                }

                _synth = null;
            }
        }
    }
}
