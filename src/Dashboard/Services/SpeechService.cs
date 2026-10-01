using System.Runtime.Versioning;
using System.Speech.Synthesis;
using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The spoken alerts, through Windows' own speech engine (SAPI5, via <c>System.Speech</c>) rather than the browser's.
    /// The sound comes from the PC running the dashboard, whatever browser shows the page. Alerts queue rather than
    /// interrupt each other: a newer one waits its turn, with a short pause after the one before it, so two that land
    /// close together (a phase finishing and the next one starting, say) are both heard in full instead of the second
    /// cutting the first off mid-word. Where Windows speech is not available, alerts are simply silent.
    ///
    /// The voice is picked by name (a full SAPI5 name, or part of one, case-insensitive: "Zira" is enough to find
    /// "Microsoft Zira Desktop") — <b>not</b> whatever Windows' own Settings app (Time &amp; language &gt; Speech &gt;
    /// "Choose a voice") has picked. That picker, and the legacy "default" SAPI5 voice this engine actually starts with,
    /// are two different lists in Windows; changing one does not change the other, so choosing a voice there can look
    /// like it is simply being ignored. Asking for one by name here sidesteps that entirely. The starting name comes
    /// from <c>Speech:Voice</c> in appsettings.json (blank leaves Windows' own default alone); picking one instead from
    /// the Home page's dropdown (<see cref="SetVoice"/>) overrides that, remembered in <c>speech-settings.json</c> so it
    /// is still picked the next time the dashboard starts. The volume slider there is remembered the same way.
    /// </summary>
    public sealed class SpeechService : IDisposable
    {
        // The browser voice ran at 0.92 of normal speed; the nearest step on Windows' -10 to 10 scale.
        private const int Rate = -1;

        /// <summary>How long to leave between one queued phrase finishing and the next starting.</summary>
        private static readonly TimeSpan PauseBetween = TimeSpan.FromMilliseconds(600);

        private sealed class Settings
        {
            /// <summary>The owner's own pick (<see cref="SetVoice"/>), by exact SAPI5 name; null until they choose one.</summary>
            public string? Voice { get; set; }

            public double Volume { get; set; } = 0.8;
        }

        private readonly ILogger<SpeechService> _log;
        private readonly string? _configuredVoiceHint;
        private readonly string _settingsPath;
        private readonly object _lock = new();
        private readonly Dictionary<string, DateTime> _lastSpoken = new();
        private readonly Queue<(string Text, double Volume)> _queue = new();
        private Settings _settings;
        private SpeechSynthesizer? _synth;
        private bool _unavailable;

        /// <summary>True from the moment something starts speaking until the queue is empty again (through the pauses between).</summary>
        private bool _busy;

        public SpeechService(ILogger<SpeechService> log, IConfiguration config)
        {
            _log = log;
            _configuredVoiceHint = LivePaths.Setting(config, "Speech:Voice");
            _settingsPath = LivePaths.SpeechSettings(config);
            _settings = LoadSettings();
        }

        /// <summary>The volume slider's value (0 to 1) as last set, or the default on a dashboard that has never set one.</summary>
        public double Volume => _settings.Volume;

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

        /// <summary>Remembers the volume slider's value for next time (does not itself say anything).</summary>
        public void SetVolume(double volume)
        {
            lock (_lock)
            {
                _settings.Volume = Math.Clamp(volume, 0, 1);
                SaveSettingsLocked();
            }
        }

        /// <summary>Every enabled voice Windows has installed, for a picker; empty where speech is not available.</summary>
        public IReadOnlyList<string> AvailableVoices()
        {
            if (!OperatingSystem.IsWindows())
                return Array.Empty<string>();

            lock (_lock)
            {
                var synth = Synth();
                if (synth is null)
                    return Array.Empty<string>();

                // A plain loop, not GetInstalledVoices().Where(...).Select(...): the platform-compatibility analyzer
                // does not see through a lambda's own body to this method's OperatingSystem.IsWindows() guard above.
                var names = new List<string>();
                foreach (var voice in synth.GetInstalledVoices())
                {
                    if (voice.Enabled)
                        names.Add(voice.VoiceInfo.Name);
                }

                return names;
            }
        }

        /// <summary>The voice in use right now, or null before anything has needed one yet or where speech is not available.</summary>
        public string? CurrentVoice()
        {
            if (!OperatingSystem.IsWindows())
                return null;

            lock (_lock)
                return _synth?.Voice.Name;
        }

        /// <summary>
        /// Picks a voice by its exact name (from <see cref="AvailableVoices"/>) and remembers it (<c>speech-settings.json</c>)
        /// so it is picked again the next time the dashboard starts, overriding appsettings.json's <c>Speech:Voice</c> from
        /// then on. Says a short confirmation in the new voice, at <paramref name="volume"/>.
        /// </summary>
        public void SetVoice(string name, double volume)
        {
            if (!OperatingSystem.IsWindows())
                return;

            lock (_lock)
            {
                var synth = Synth();
                if (synth is null)
                    return;

                try
                {
                    synth.SelectVoice(name);
                }
                catch (Exception e)
                {
                    _log.LogWarning(e, "Could not select the voice {Name}", name);
                    return;
                }

                _settings.Voice = name;
                SaveSettingsLocked();
            }

            Speak("This is " + name, volume);
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

        // Only ever subscribed from Synth() (Windows-only, and only once _synth exists), so this never actually runs
        // elsewhere; the attribute just tells the platform-compatibility analyzer what already is true.
        [SupportedOSPlatform("windows")]
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
                // SelectPreferredVoice then asks for the remembered or configured name, if there is one.
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
        /// Tries the owner's own remembered pick first, then appsettings.json's <c>Speech:Voice</c>; either way, as an
        /// exact SAPI5 name first ("Microsoft Zira Desktop"), then as a case-insensitive substring of one ("Zira" finds
        /// it too). Leaves the synthesizer's own starting voice alone when neither is set or neither matches anything
        /// installed (logged once, not a failure).
        /// </summary>
        [SupportedOSPlatform("windows")]
        private void SelectPreferredVoice(SpeechSynthesizer synth)
        {
            var hint = _settings.Voice ?? _configuredVoiceHint;
            if (string.IsNullOrWhiteSpace(hint))
                return;

            try
            {
                synth.SelectVoice(hint);
                return;
            }
            catch (Exception)
            {
                // Not an exact SAPI5 name; fall through to a substring search of what is actually installed.
            }

            var found = synth.GetInstalledVoices()
                .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));

            if (found is not null)
                synth.SelectVoice(found.VoiceInfo.Name);
            else
                _log.LogWarning("The voice \"{Hint}\" is wanted, but no installed voice matches it; using {Default} instead.", hint, synth.Voice.Name);
        }

        private Settings LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return new Settings();

                using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<Settings>(stream) ?? new Settings();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return new Settings();
            }
        }

        /// <summary>Must be called with <see cref="_lock"/> held.</summary>
        private void SaveSettingsLocked()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(ex, "Could not save speech settings to {Path}", _settingsPath);
            }
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
