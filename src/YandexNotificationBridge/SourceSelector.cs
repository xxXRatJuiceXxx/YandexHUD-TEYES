namespace YandexNotificationBridge;

internal sealed class SourceSelector
{
    private long _lastScreenEnded;
    private bool _screenWasRoute;
    internal BridgeState Select(BridgeState? screen, BridgeState notification, long now)
    {
        if (screen is not null && screen.ObservedAtUnixMilliseconds >= now - 2000 && screen.ObservedAtUnixMilliseconds <= now + 1000)
        {
            if (_screenWasRoute && !screen.Active) _lastScreenEnded = screen.SourceTimestamp;
            _screenWasRoute = screen.Active;
            // Supplement only the same maneuver from a very recent
            // notification. Never keep lanes from a preceding junction.
            if (screen.Active && notification.Active && notification.SourceTimestamp >= now - 4000 &&
                notification.SourceTimestamp <= now + 1000 && notification.SourceTimestamp >= _lastScreenEnded &&
                SameRoad(screen.Road, notification.Road) &&
                screen.NextTurnDistanceMeters == notification.NextTurnDistanceMeters &&
                notification.Maneuver is not null && notification.Maneuver != "Camera" &&
                (screen.Maneuver is null || Compatible(screen.Maneuver, notification.Maneuver)))
                return screen with { Maneuver = screen.Maneuver ?? notification.Maneuver,
                    Lanes = notification.Lanes, LanesObservedAtUnixMilliseconds = notification.LanesObservedAtUnixMilliseconds };
            return screen;
        }
        // A late cached notification must not resurrect a route ended on screen.
        return notification.Active && notification.SourceTimestamp >= _lastScreenEnded
            ? notification : BridgeState.Inactive;
    }
    internal static bool SameRoad(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return NormalizeRoad(a) == NormalizeRoad(b);
    }
    private static string NormalizeRoad(string value)
    {
        value = value.ToLowerInvariant().Replace('ё', 'е');
        // Expand road types, keeping every name/number and their order intact.
        // Never fuzzy-match adjacent streets or roads with similar names.
        value = System.Text.RegularExpressions.Regex.Replace(value,
            @"(?<![\p{L}\p{N}])(?:ул\.|улица)(?![\p{L}\p{N}])", "улица");
        value = System.Text.RegularExpressions.Regex.Replace(value,
            @"(?<![\p{L}\p{N}])(?:пр-т|просп\.|проспект)(?![\p{L}\p{N}])", "проспект");
        value = System.Text.RegularExpressions.Regex.Replace(value,
            @"(?<![\p{L}\p{N}])(?:пер\.|переулок)(?![\p{L}\p{N}])", "переулок");
        value = System.Text.RegularExpressions.Regex.Replace(value,
            @"(?<![\p{L}\p{N}])(?:ш\.|шоссе)(?![\p{L}\p{N}])", "шоссе");
        return System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();
    }
    private static bool Compatible(string a, string b) => a == b ||
        a is "RightFork" or "RightWide" && b is "RightFork" or "RightWide" ||
        a is "LeftFork" or "LeftWide" && b is "LeftFork" or "LeftWide";
}
