using System.Runtime.Versioning;
using System.Speech.Synthesis;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The spoken alerts, through Windows' own speech engine rather than the browser's. So they use the default voice set
    /// in Windows (Settings > Time &amp; language > Speech), whatever browser shows the page. The sound comes from the PC
    /// running the dashboard. A newer alert cuts off one still being spoken. Where Windows speech is not available,
    /// alerts are simply silent.
    /// </summary>
    public sealed class SpeechService : IDisposable
    {
        // The browser voice ran at 0.92 of normal speed; the nearest step on Windows' -10 to 10 scale.
        private const int Rate = -1;

        private readonly ILogger<SpeechService> _log;
        private readonly object _lock = new();
        private readonly Dictionary<string, DateTime> _lastSpoken = new();
        private SpeechSynthesizer? _synth;
        private bool _unavailable;

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

        /// <summary>Says <paramref name="text"/> at <paramref name="volume"/> (0 to 1), cutting off anything still being said.</summary>
        public void Speak(string text, double volume)
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
                    synth.SpeakAsyncCancelAll();
                    synth.Volume = (int)Math.Round(Math.Clamp(volume, 0, 1) * 100);
                    synth.SpeakAsync(text);
                }
                catch (Exception e)
                {
                    _log.LogWarning(e, "Could not speak an alert");
                }
            }
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
                if (OperatingSystem.IsWindows())
                    _synth?.Dispose();
                _synth = null;
            }
        }
    }
}
