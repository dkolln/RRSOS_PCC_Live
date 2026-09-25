namespace RRSOS.PCC.Dashboard
{
    /// <summary>One row of the Cheats page's Resupply list: a container to look for by its own text, and the
    /// product that container should hold. <see cref="Id"/> is a client-generated key so the UI can track a row
    /// across edits regardless of its content. With <see cref="ReplaceAll"/> every item already in the container
    /// becomes the product too; without it only the empty slots are filled. Configs saved before the setting
    /// existed read as true, which is what they always did.</summary>
    public sealed record ResupplyConfig(string Id, string ContainerLabel, string ProductGId, string ProductName, bool ReplaceAll = true);

    /// <summary>What <see cref="RRSOS.PCC.Dashboard.ResupplyConfigStore"/> persists: the configs plus which save
    /// file they were last pointed at.</summary>
    public sealed class ResupplyConfigFile
    {
        public string? SelectedSaveFile { get; set; }
        public List<ResupplyConfig> Configs { get; set; } = new();

        /// <summary>Also fill any container whose label is an item id ("MalisseaH"), after the configs. On unless turned off.</summary>
        public bool AutoFillByGId { get; set; } = true;

        /// <summary>For those containers: also replace what is already in them. Off: only their empty slots are filled.</summary>
        public bool AutoFillReplaceAll { get; set; }
    }

    /// <summary>What a Resupply run does beyond the configured rows (see <see cref="ResupplyConfigFile"/>).</summary>
    public sealed record ResupplyOptions(bool AutoFillByGId, bool AutoFillReplaceAll);
}
