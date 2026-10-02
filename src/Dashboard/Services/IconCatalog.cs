namespace RRSOS.PCC.Dashboard
{
    /// <summary>Which of the game's icons the plugin has written (plugin 0.12.0, see IconExporter), for a place that draws something else when there is none.</summary>
    public sealed class IconCatalog
    {
        private readonly string _folder;
        private readonly HashSet<string> _have = new(StringComparer.Ordinal);

        public IconCatalog(IConfiguration config) => _folder = Path.Combine(LivePaths.Folder(config), "icons");

        /// <summary>True when there is an icon file for the group id. Found ones are remembered; a missing one is looked for again, since the plugin may write it later.</summary>
        public bool Has(string id)
        {
            lock (_have)
            {
                if (_have.Contains(id))
                    return true;

                if (Path.GetFileName(id) != id || !File.Exists(Path.Combine(_folder, id + ".png")))
                    return false;

                _have.Add(id);
                return true;
            }
        }
    }
}
