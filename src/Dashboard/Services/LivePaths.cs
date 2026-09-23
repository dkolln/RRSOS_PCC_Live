namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Where the plugin's files live. Normally <c>%LOCALAPPDATA%\RRSOS-PCC-Live</c> of whoever runs the game, which is
    /// right on any PC. <c>LiveFolder</c> moves them all; pointing <c>LiveFile</c> somewhere else (the fake data from
    /// tools/sample-live.ps1) moves everything beside it, so trying the dashboard without the game never touches the real
    /// folder or the real base names. A setting left blank (as appsettings.json lists them) counts as not set.
    /// </summary>
    public static class LivePaths
    {
        public static string Folder(IConfiguration config)
        {
            if (Setting(config, "LiveFolder") is { } folder)
                return Path.GetFullPath(folder);

            return Setting(config, "LiveFile") is { } live
                ? Path.GetDirectoryName(Path.GetFullPath(live)) ?? "."
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RRSOS-PCC-Live");
        }

        public static string LiveFile(IConfiguration config) =>
            Setting(config, "LiveFile") ?? Path.Combine(Folder(config), "live.json");

        public static string WorldFile(IConfiguration config) =>
            Setting(config, "WorldFile") ?? Path.Combine(Folder(config), "live-world.json");

        public static string BaseData(IConfiguration config) =>
            Setting(config, "BaseData") ?? Path.Combine(Folder(config), "basedata.json");

        /// <summary>A setting's value, or null when it is missing or blank.</summary>
        public static string? Setting(IConfiguration config, string key) =>
            string.IsNullOrWhiteSpace(config[key]) ? null : config[key];
    }
}
