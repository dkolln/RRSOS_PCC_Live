using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace RRSOS.PCC.Build
{
    /// <summary>
    /// The live-write side of the companion (see README, "The read-only cutoff"). A separate plugin from RRSOS.PCC.Live on purpose: that one only observes.
    /// This one places the first stage of the main base in the running game, once, at planetfall. Off unless <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.build";
        public const string Name = "RRSOS PCC Build";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> UndoLastBuild;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", false, "Place the first stage of the base (anchor lamp, two foundations, one pod) when a new world has none of it. Writes into the running game.");
            UndoLastBuild = Config.Bind("General", "UndoLastBuild", false, "Set to true and load the world: removes what this plugin placed in that world, then sets itself back to false.");
            gameObject.AddComponent<BaseBuilder>();
            Log.LogInfo($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {BaseBuilder.LogFile}");
        }
    }
}
