using System.Runtime.Versioning;
using System.Speech.Synthesis;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The spoken alerts, through Windows' own speech engine (SAPI5, via <c>System.Speech</c>) rather than the browser's.
    /// The sound comes from the PC running the dashboard, whatever browser shows the page. Alerts queue rather than
    /// interrupt each other: a newer one waits its turn, with a short pause after the one before it, so two that land
    /// close together (a phase finishing and the next one starting, say) are both heard in full instead of the second
    /// cutting the first off mid-word. Where Windows speech is not available, alerts are simply silent.
    ///
    /// The voice is <see cref="VoiceHint"/> (a name, or part of one, case-insensitive: "Zira" is enough to find
    /// "Microsoft Zira Desktop"), from the <c>Speech:Voice</c> setting — <b>not</b> whatever Windows' own Settings app
    /// (Time &amp; language &gt; Speech &gt; "Choose a voice") has picked. That picker, and the legacy "default" SAPI5
    /// voice this engine actually starts with, are two different lists in Windows; changing one does not change the
    /// other, so choosing a voice there can look like it is simply being ignored. Asking for one by name here sidesteps
    /// that entirely.
    /// </summary>
    public sealed class SpeechService : IDisposable
    {
        // The browser voice ran at 0.92 of normal speed; the nearest step on Windows' -10 to 10 scale.
        private const int Rate = -1;

        /// <summary>How long to leave between one queued phrase finishing and the next starting.</summary>
        private static readonly TimeSpan PauseBetween = TimeSpan.FromMilliseconds(600);

        private readonly ILogger<SpeechService> _log;
        private readonly string? _voiceHint;
        private readonly object _lock = new();
        private readonly Dictionary<string, DateTime> _lastSpoken = new();
        private readonly Queue<(string Text, double Volume)> _queue = new();
        private SpeechSynthesizer? _synth;
        private bool _unavailable;

        /// <summary>True from the moment something starts speaking until the queue is empty again (through the pauses between).</summary>
        private bool _busy;

        public SpeechService(ILogger<SpeechService> log, IConfiguration config)
        {
            _log = log;
            _voiceHint = LivePaths.Setting(config, "Speech:Voice");
        }

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
                // A new synthesizer starts with Windows' legacy SAPI5 default voice and the default audio device;
                // SelectPreferredVoice then asks for Speech:Voice by name, if that setting is not blank.
                _synth = new SpeechSynthesizer { Rate = Rate };
                _synth.SetOutputToDefaultAudioDevice();
                SelectPreferredVoice(_synth);
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

        /// <summary>
        /// Tries <see cref="_voiceHint"/> as an exact SAPI5 voice name first ("Microsoft Zira Desktop"), then as a
        /// case-insensitive substring of one ("Zira" finds it too); leaves the synthesizer's own starting voice alone
        /// when the hint is blank or matches nothing installed (logged once, not a failure).
        /// </summary>
        [SupportedOSPlatform("windows")]
        private void SelectPreferredVoice(SpeechSynthesizer synth)
        {
            if (string.IsNullOrWhiteSpace(_voiceHint))
                return;

            try
            {
                synth.SelectVoice(_voiceHint);
                return;
            }
            catch (Exception)
            {
                // Not an exact SAPI5 name; fall through to a substring search of what is actually installed.
            }

            var found = synth.GetInstalledVoices()
                .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Name.Contains(_voiceHint, StringComparison.OrdinalIgnoreCase));

            if (found is not null)
                synth.SelectVoice(found.VoiceInfo.Name);
            else
                _log.LogWarning("Speech:Voice is set to \"{Hint}\", but no installed voice matches it; using {Default} instead.", _voiceHint, synth.Voice.Name);
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
