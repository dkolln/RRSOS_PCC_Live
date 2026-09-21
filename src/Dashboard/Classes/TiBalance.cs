namespace RRSOS.PCC.Dashboard
{
    public enum TiStatus
    {
        /// <summary>Not started (under the minimum TI). Not counted, no colour.</summary>
        Idle,

        /// <summary>At or above an equal share, or the planet is balanced.</summary>
        Good,

        /// <summary>Below an equal share.</summary>
        Warn,

        /// <summary>Far below an equal share.</summary>
        Crit
    }

    /// <param name="Share">This category's part of the total, in percent points (0 to 100).</param>
    /// <param name="Deviation">Share minus an equal share, in points. Negative means behind.</param>
    /// <param name="ActiveCount">How many categories have any TI, i.e. what "equal share" was divided among.</param>
    /// <param name="Balanced">The spread between the best and worst category was small enough that nothing is flagged.</param>
    public sealed record TiCategory(string Name, float Value, float Share, float Deviation, TiStatus Status, int ActiveCount, bool Balanced)
    {
        /// <summary>What an equal share would have been, in percent points.</summary>
        public float FairShare => ActiveCount > 0 ? 100f / ActiveCount : 0f;
    }

    /// <summary>
    /// Works out how evenly the terraformation index is spread, as a to-do guide for what to build next.
    ///
    /// Only categories that have at least <see cref="MinimumTi"/> take part. Each is compared with
    /// an equal split of the total among them, and the difference in percent points is its
    /// deviation. A category below that is left out of the maths entirely and just shows as idle.
    /// </summary>
    public static class TiBalance
    {
        /// <summary>A category is not considered until it has at least this much TI.</summary>
        public const float MinimumTi = 1f;

        /// <summary>The dial covers this many points either side of "exactly fair".</summary>
        public const float ScalePoints = 50f;

        /// <summary>
        /// Colours: the most-behind category is always red as long as it really is behind (below
        /// an equal share), so there is always one clear "do this next". Any other category this
        /// many points behind or worse is red too. Everything else below an equal share is
        /// yellow, and at or above an equal share is green.
        /// </summary>
        public const float RedAtOrBelow = -30f;

        /// <summary>If even the biggest gap between two categories is under this, nothing is flagged.</summary>
        public const float BalancedSpread = 5f;

        // An idle category still shows how much TI it does have (say 0.4), never something non-numeric.
        private static float IdleValue(float value) => float.IsFinite(value) && value > 0f ? value : 0f;

        public static IReadOnlyList<TiCategory> Evaluate(IReadOnlyList<(string Name, float Value)> categories)
        {
            // Anything under the minimum, and negative or non-numeric values, count as "not started".
            static bool IsActive(float value) => float.IsFinite(value) && value >= MinimumTi;

            var total = categories.Where(c => IsActive(c.Value)).Sum(c => (double)c.Value);
            var activeCount = categories.Count(c => IsActive(c.Value));

            if (activeCount == 0 || total <= 0)
                return categories.Select(c => new TiCategory(c.Name, IdleValue(c.Value), 0f, 0f, TiStatus.Idle, 0, false)).ToList();

            var fair = 100f / activeCount;

            var shares = categories.Select(c => IsActive(c.Value) ? (float)(c.Value / total * 100.0) : 0f).ToList();

            var deviations = shares
                .Select((share, i) => IsActive(categories[i].Value) ? share - fair : 0f)
                .ToList();

            var activeDeviations = deviations.Where((_, i) => IsActive(categories[i].Value)).ToList();
            var lowest = activeDeviations.Min();
            var balanced = activeDeviations.Max() - lowest < BalancedSpread;

            // If two are exactly level at the bottom, the first in display order is the one to do next.
            var lowestIndex = Enumerable.Range(0, categories.Count).First(i => IsActive(categories[i].Value) && deviations[i] == lowest);

            return categories.Select((c, i) =>
            {
                if (!IsActive(c.Value))
                    return new TiCategory(c.Name, IdleValue(c.Value), 0f, 0f, TiStatus.Idle, activeCount, balanced);

                var deviation = deviations[i];

                var status = balanced || deviation >= 0f ? TiStatus.Good
                    : i == lowestIndex || deviation <= RedAtOrBelow ? TiStatus.Crit
                    : TiStatus.Warn;

                return new TiCategory(c.Name, c.Value, shares[i], deviation, status, activeCount, balanced);
            }).ToList();
        }
    }
}
