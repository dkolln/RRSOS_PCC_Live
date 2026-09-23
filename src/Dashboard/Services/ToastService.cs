namespace RRSOS.PCC.Dashboard
{
    public enum ToastKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <param name="Life">How long it stays fully visible before it fades out.</param>
    /// <param name="Delay">How long to wait before it appears, so several toasts can cascade in.</param>
    public sealed record Toast(long Id, string Text, ToastKind Kind, TimeSpan Life, TimeSpan Delay);

    /// <summary>
    /// Short flash messages that fade away on their own. One per browser session (scoped), so a toast only ever
    /// shows in the page that caused it. The fade itself is CSS; this just keeps the list and removes each toast
    /// once it has finished. Ported from RRSOS-PCC for the Cheats page's Resupply results.
    /// </summary>
    public sealed class ToastService
    {
        public static readonly TimeSpan DefaultLife = TimeSpan.FromSeconds(4);

        // The CSS fade-out is part of the animation, so give it a moment past Life before removing the element.
        private static readonly TimeSpan FadeTail = TimeSpan.FromMilliseconds(600);

        // A runaway loop of messages must not bury the page.
        private const int MaxVisible = 8;

        private readonly object _lock = new();
        private readonly List<Toast> _toasts = new();
        private long _nextId;

        /// <summary>Raised whenever a toast is added or removed (possibly from a background thread).</summary>
        public event Action? Changed;

        public IReadOnlyList<Toast> Current
        {
            get { lock (_lock) return _toasts.ToArray(); }
        }

        public void Show(string text, ToastKind kind = ToastKind.Info, TimeSpan? life = null, TimeSpan? delay = null)
        {
            Toast toast;

            lock (_lock)
            {
                toast = new Toast(++_nextId, text, kind, life ?? DefaultLife, delay ?? TimeSpan.Zero);
                _toasts.Add(toast);

                while (_toasts.Count > MaxVisible)
                    _toasts.RemoveAt(0);
            }

            Changed?.Invoke();
            _ = RemoveWhenDone(toast);
        }

        private async Task RemoveWhenDone(Toast toast)
        {
            await Task.Delay(toast.Delay + toast.Life + FadeTail);

            bool removed;
            lock (_lock)
                removed = _toasts.Remove(toast);

            if (removed)
                Changed?.Invoke();
        }
    }
}
