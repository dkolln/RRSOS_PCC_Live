namespace RRSOS.PCC.Dashboard
{
    /// <summary>One row of the Cheats page's Resupply list: a container to look for by its own text, and the
    /// product that container should hold. <see cref="Id"/> is a client-generated key so the UI can track a row
    /// across edits regardless of its content.</summary>
    public sealed record ResupplyConfig(string Id, string ContainerLabel, string ProductGId, string ProductName);

    /// <summary>What <see cref="RRSOS.PCC.Dashboard.ResupplyConfigStore"/> persists: the configs plus which save
    /// file they were last pointed at.</summary>
    public sealed class ResupplyConfigFile
    {
        public string? SelectedSaveFile { get; set; }
        public List<ResupplyConfig> Configs { get; set; } = new();
    }
}
