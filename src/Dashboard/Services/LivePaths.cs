namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Where the plugin's files live. Normally <c>%LOCALAPPDATA%\RRSOS-PCC-Live</c>; pointing <c>LiveFile</c> somewhere
    /// else (the fake data from tools/sample-live.ps1) moves everything beside it, so trying the dashboard without the
    /// game never touches the real folder or the real base names.
    /// </summary>
    public static class LivePaths
    {
        public static string Folder(IConfiguration config)
        {
            var live = config["LiveFile"];

            return string.IsNullOrWhiteSpace(live)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RRSOS-PCC-Live")
                : Path.GetDirectoryName(Path.GetFullPath(live)) ?? ".";
        }

        public static string LiveFile(IConfiguration config) =>
            config["LiveFile"] ?? Path.Combine(Folder(config), "live.json");

        public static string WorldFile(IConfiguration config) =>
            config["WorldFile"] ?? Path.Combine(Folder(config), "live-world.json");

        public static string BaseData(IConfiguration config) =>
            config["BaseData"] ?? Path.Combine(Folder(config), "basedata.json");
    }
}
