namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The Cheats page's Travel tab: reads which planets a save knows about and which of them have a warehouse beacon
    /// (see <see cref="PlayerTravelEngine"/>), and moves the player to a spot on one, with the same backup-first,
    /// undo-proven write as every other Cheats edit (<see cref="SaveResupplyService.EditAsync{T}"/>). The game must be
    /// at its main menu.
    /// </summary>
    public sealed class PlayerTravelService
    {
        private readonly SaveResupplyService _saves;

        public PlayerTravelService(SaveResupplyService saves) => _saves = saves;

        public Task<IReadOnlyList<TravelPlanet>> PlanetsAsync(string savePath) => Task.Run(() =>
        {
            var text = BaseBuildingService.ReadText(savePath, out _);
            return text is null ? (IReadOnlyList<TravelPlanet>)Array.Empty<TravelPlanet>() : PlayerTravelEngine.Planets(text);
        });

        /// <summary>That planet's own warehouse beacon, or null when it has none — for "no location: use the beacon".</summary>
        public Task<TravelDestination?> BeaconOnAsync(string savePath, int planetHash) => Task.Run(() =>
        {
            var text = BaseBuildingService.ReadText(savePath, out _);
            return text is null ? null : PlayerTravelEngine.BeaconOn(text, planetHash);
        });

        public Task<SaveEdit<bool>> TravelToPositionAsync(string savePath, double x, double y, double z, string planetId) =>
            _saves.EditAsync<bool>(savePath, text =>
            {
                var outcome = PlayerTravelEngine.MoveToPosition(text, x, y, z, planetId);
                return outcome.Failed ? ((string?)null, false, outcome.Error) : (outcome.NewText, true, (string?)null);
            }, "moving the player");
    }
}
