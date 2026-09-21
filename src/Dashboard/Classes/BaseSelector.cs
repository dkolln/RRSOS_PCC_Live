namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Decides which base the Base tab shows. Standing between two bases must not make the contents flip back and forth
    /// every refresh, so the current base is kept until another one is clearly closer. Ported from RRSOS-PCC.
    /// </summary>
    public static class BaseSelector
    {
        /// <param name="basesNearestFirst">Bases ordered nearest to the player first.</param>
        /// <param name="currentId">The base currently shown, if any.</param>
        /// <param name="switchThreshold">How much closer another base must be before switching.</param>
        public static long? Choose(IReadOnlyList<BaseView>? basesNearestFirst, long? currentId, float switchThreshold)
        {
            if (basesNearestFirst is null || basesNearestFirst.Count == 0)
                return null;

            var closest = basesNearestFirst[0];

            if (currentId is not long id)
                return closest.Id;

            var current = basesNearestFirst.FirstOrDefault(b => b.Id == id);

            // The base we were showing is gone (other save, base removed).
            if (current is null)
                return closest.Id;

            if (closest.Id != current.Id && closest.Distance < current.Distance - switchThreshold)
                return closest.Id;

            return current.Id;
        }
    }
}
