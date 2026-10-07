namespace RRSOS.PCC.Dashboard
{
    /// <summary>One building that makes a planet stat: how many stand on the planet, what one makes per second, what they make together right now, and whether it is unlocked.</summary>
    public sealed class TerraformerEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Count { get; set; }

        /// <summary>How many of those are making some of it right now. A planter or spreader makes its stat only while it holds something. -1 when the plugin does not say (all of them then).</summary>
        public int Active { get; set; } = -1;

        /// <summary>How many are working: <see cref="Active"/>, or all of them when the plugin did not say.</summary>
        public int Working => Active < 0 ? Count : Math.Min(Active, Count);

        /// <summary>What one of it makes per second, from the game's building data.</summary>
        public double Each { get; set; }

        /// <summary>What all of it on the planet makes per second right now (each machine's own live figure, boosts included).</summary>
        public double Total { get; set; }

        public bool Unlocked { get; set; }

        public string Label => string.IsNullOrWhiteSpace(Name) ? Id : Name;
    }

    /// <summary>The plugin's <c>terraformers.json</c> (plugin 0.13.0): for each planet stat ("oxygen", "heat", "pressure", "plants", "insects", "animals", "purification"), every building that makes it.</summary>
    public sealed class TerraformerFile
    {
        public int PlanetHash { get; set; }
        public Dictionary<string, List<TerraformerEntry>> Units { get; set; } = new();
    }
}
