using BepInEx;
using BepInEx.Logging;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Entry point. Module 0: it only proves the plugin loads. It reports game state (in later
    /// modules) and never changes it.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.live";
        public const string Name = "RRSOS PCC Live";
        public const string Version = "0.0.1";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{Name} {Version} loaded. Read-only: it reports game state and never changes it.");
        }
    }
}
