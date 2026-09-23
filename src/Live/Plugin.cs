using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Entry point. Reports game state to a JSON file (see docs/contract.md) and never changes it.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.live";
        public const string Name = "RRSOS PCC Live";
        public const string Version = "0.6.0";

        private const int MaxRepeats = 3;

        internal static ManualLogSource Log;

        private static readonly Dictionary<string, int> Reported = new Dictionary<string, int>();

        private void Awake()
        {
            Log = Logger;
            gameObject.AddComponent<Poller>();
            gameObject.AddComponent<WorldPoller>();
            Log.LogInfo($"{Name} {Version} loaded. Read-only: it reports game state and never changes it. Live file: {LiveFile.FilePath}; world file: {LiveFile.WorldFilePath}");
        }

        /// <summary>Logs a warning, but only the first few times for each key, so a problem that repeats every second cannot flood the log.</summary>
        internal static void LogOnce(string key, string message)
        {
            Reported.TryGetValue(key, out var seen);
            if (seen >= MaxRepeats)
                return;

            Reported[key] = seen + 1;
            Log.LogWarning(seen + 1 == MaxRepeats ? message + " (further repeats are not logged)" : message);
        }
    }
}
