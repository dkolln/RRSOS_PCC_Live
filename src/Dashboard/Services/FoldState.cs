namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// Which collapsible headers the user has clicked, kept for as long as the dashboard runs so a fold survives the screen switching away
    /// (to the drone view and back) and coming back. A header is remembered as "flipped from its default", so each one still has its own default.
    /// </summary>
    public sealed class FoldState
    {
        private readonly HashSet<string> _flipped = new(StringComparer.Ordinal);
        private readonly object _lock = new();

        /// <summary>True when the header with this key is open, given whether it starts open.</summary>
        public bool IsOpen(string key, bool defaultOpen)
        {
            lock (_lock)
                return _flipped.Contains(key) ? !defaultOpen : defaultOpen;
        }

        /// <summary>The user clicked the header: open becomes folded and folded becomes open.</summary>
        public void Flip(string key)
        {
            lock (_lock)
            {
                if (!_flipped.Remove(key))
                    _flipped.Add(key);
            }
        }
    }
}
