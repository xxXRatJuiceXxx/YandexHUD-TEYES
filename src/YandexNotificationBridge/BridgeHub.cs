namespace YandexNotificationBridge;

internal static class BridgeHub
{
    private static readonly object Gate = new();
#if !HEADUNIT
    private static BridgeServer? _server;
#endif
    private static long _sequence;
    private static readonly SourceSelector Selector = new();
    private static BridgeState _current = BridgeState.Inactive;
    internal static BridgeState Current => Volatile.Read(ref _current);
    internal static void Start()
    {
#if !HEADUNIT
        lock (Gate)
        {
            if (_server is not null) return;
            var server = new BridgeServer();
            server.Start();
            _server = server;
        }
#endif
    }
    internal static async Task<BridgeState> ReadAsync()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var screen = YandexScreenService.Current;
        BridgeState notification = BridgeState.Inactive;
        if (screen is null || screen.ObservedAtUnixMilliseconds < now - 2000 || screen.Active)
        {
            try { notification = await YandexBridgeNotificationListenerService.ReadVerifiedAsync().WaitAsync(TimeSpan.FromMilliseconds(900)); }
            catch (Exception) { }
        }
        now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (Gate)
        {
            var state = Selector.Select(screen, notification, now) with { ScreenReaderConnected = YandexScreenService.Connected };
            var comparison = state with { Sequence = 0, UpdatedAtUnixMilliseconds = 0, ObservedAtUnixMilliseconds = 0, CameraObservedAtUnixMilliseconds = 0, SourceTimestamp = 0, LanesObservedAtUnixMilliseconds = 0 };
            var previous = _current with { Sequence = 0, UpdatedAtUnixMilliseconds = 0, ObservedAtUnixMilliseconds = 0, CameraObservedAtUnixMilliseconds = 0, SourceTimestamp = 0, LanesObservedAtUnixMilliseconds = 0 };
            bool changed = comparison != previous;
            if (changed) _sequence++;
            state = state with { Sequence = _sequence, UpdatedAtUnixMilliseconds = changed ? now : _current.UpdatedAtUnixMilliseconds,
                ObservedAtUnixMilliseconds = now };
            Volatile.Write(ref _current, state);
            return state;
        }
    }
}
