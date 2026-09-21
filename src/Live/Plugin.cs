using BepInEx;
using BepInEx.Logging;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Entry point. Reports game state to a JSON file (see docs/contract.md) and never changes it.
    /// Module 3: the local player's position, heading and vitals.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.live";
        public const string Name = "RRSOS PCC Live";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            gameObject.AddComponent<Poller>();
            Log.LogInfo($"{Name} {Version} loaded. Read-only: it reports game state and never changes it. Live file: {LiveFile.FilePath}");
        }
    }
}
