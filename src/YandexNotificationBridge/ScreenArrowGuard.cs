namespace YandexNotificationBridge;

internal static class ScreenArrowGuard
{
    internal static ScreenArea? Region(IReadOnlyList<ScreenField> fields)
    {
        var arrows = fields.Where(f => ScreenNavigationReader.IsCurrentArrow(f.Id)).ToArray();
        // Never guess screen coordinates or choose between multiple balloons.
        return arrows.Length == 1 && arrows[0].Area is { ValidArrow: true } area ? area : null;
    }

    internal static bool SameFrame(BridgeState before, BridgeState after,
        ScreenArea? region, ScreenArea? currentRegion, int window, int currentWindow, long elapsed)
        => elapsed is >= 0 and <= 1200 && window == currentWindow && region is { ValidArrow: true } && region == currentRegion &&
           before.Active && after.Active && before.Mode == "route" && after.Mode == "route" &&
           before.Road == after.Road && before.NextTurnDistanceMeters == after.NextTurnDistanceMeters;
}
