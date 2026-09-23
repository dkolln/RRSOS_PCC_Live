namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Finds the game's save folder and lists what is in it. Trimmed from RRSOS-PCC's PathResolver: this app only
    /// ever reads saves for the Cheats page, it never owns where the game keeps them.
    /// </summary>
    public static class SaveFolderResolver
    {
        public static string DetectFolder(IConfiguration config)
        {
            var configured = config["SaveSettings:SavePath"];
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
                return configured;

            // The game keeps saves in LocalLow, which has no SpecialFolder entry.
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Path.GetDirectoryName(localAppData) ?? localAppData;
            return Path.Combine(appData, "LocalLow", "MijuGames", "Planet Crafter");
        }

        public static IReadOnlyList<string> ListSaveFiles(string folder)
        {
            try
            {
                if (!Directory.Exists(folder))
                    return Array.Empty<string>();

                return Directory.EnumerateFiles(folder, "*.json")
                    .Select(f => Path.GetFileName(f)!)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }
    }
}
