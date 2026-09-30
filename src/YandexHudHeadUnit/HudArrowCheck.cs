using WiiYiiHudNavigator.Common.Models;

namespace YandexHudHeadUnit;

internal static class HudArrowCheck
{
    // Explicit user-started stationary check; never saved or resumed after a disconnect.
    internal static HudNavigationData? At(long elapsedMilliseconds)
    {
        if (elapsedMilliseconds is < 0 or >= 12000) return null;
        var (direction, label) = (elapsedMilliseconds / 3000) switch
        {
            0 => (DirectionInstruction.Left, "TEST LEFT"),
            1 => (DirectionInstruction.Right, "TEST RIGHT"),
            2 => (DirectionInstruction.UTurnLeft, "TEST U-LEFT"),
            _ => (DirectionInstruction.UTurnRight, "TEST U-RIGHT")
        };
        var data = HudProtocol.Clear();
        data.NextTurnDirection = direction;
        data.NextTurnRoadName = label;
        data.NextTurnDistance = 100;
        return data;
    }
}
