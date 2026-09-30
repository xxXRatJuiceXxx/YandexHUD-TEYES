using WiiYiiHudNavigator.Common.Models;

namespace WiiYiiHudNavigator.Plugin.Yandex;

public sealed record BridgeState(
    bool Active, string? Maneuver, double NextTurnDistanceMeters, string? Road,
    double RemainingDistanceMeters, double RemainingTimeSeconds,
    long UpdatedAtUnixMilliseconds, long Sequence,
    long ObservedAtUnixMilliseconds = 0, int ProtocolVersion = 1,
    double CameraDistanceMeters = 0, long CameraObservedAtUnixMilliseconds = 0,
    string Source = "notification", string Mode = "route", string? CurrentRoad = null,
    int SpeedLimitKph = 0, string? RoadEvent = null, double RoadEventDistanceMeters = 0,
    bool ScreenReaderConnected = false, long SourceTimestamp = 0,
    string? Lanes = null, long LanesObservedAtUnixMilliseconds = 0)
{
    public HudNavigationData? ToHudData(bool latinText = true, bool showLanes = true)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (ProtocolVersion < 2 || ObservedAtUnixMilliseconds < now - 5000 || ObservedAtUnixMilliseconds > now + 5000 ||
            (!Active && ProtocolVersion < 4) || !Valid(NextTurnDistanceMeters, 2_000_000) ||
            !Valid(RemainingDistanceMeters, 50_000_000) || !Valid(RemainingTimeSeconds, 3_000_000))
            return null;
        DirectionInstruction? direction = Maneuver switch
        {
            "Right" => DirectionInstruction.Right,
            "Left" => DirectionInstruction.Left,
            "Straight" => DirectionInstruction.Straight,
            "RightWide" => DirectionInstruction.RightWide,
            "LeftWide" => DirectionInstruction.LeftWide,
            "RightSharp" => DirectionInstruction.RightSharpSE,
            "LeftSharp" => DirectionInstruction.LeftSharpSE,
            "RightFork" => DirectionInstruction.RightFork,
            "LeftFork" => DirectionInstruction.LeftFork,
            "UTurnRight" => DirectionInstruction.UTurnRight,
            "UTurnLeft" => DirectionInstruction.UTurnLeft,
            "Finish" => DirectionInstruction.Finish,
            "Rerouting" => DirectionInstruction.Rerouting,
            "Camera" when ProtocolVersion >= 3 => (DirectionInstruction)(-1),
            _ => null
        };
        if (direction is null && ProtocolVersion < 4)
            return null;
        // Unknown=13 in the host converter is a parking symbol on the device.
        // Its default branch is wire direction 0. Preserve road/ETA/warnings
        // independently when a turn cannot be recognized, instead of sending P.
        bool hasTurn = Active && direction is not null && Maneuver != "Camera";
        bool driving = ProtocolVersion < 4 || Mode == "route" || Mode == "free";
        return new HudNavigationData
        {
            NextTurnDirection = hasTurn ? direction : (DirectionInstruction)(-1),
            NextTurnDistance = hasTurn ? NextTurnDistanceMeters : 0,
            NextTurnRoadName = driving ? HudText.Encode(HudRoadText(), latinText) : "-",
            EtaDistanceRemaining = Active ? RemainingDistanceMeters : 0,
            EtaDurationTimeRemaining = Active ? RemainingTimeSeconds : 0,
            IsRerouting = direction == DirectionInstruction.Rerouting,
            CameraDistance = !driving || Maneuver == "Rerouting" || ProtocolVersion < 3 ||
                CameraObservedAtUnixMilliseconds < now - 10000 || CameraObservedAtUnixMilliseconds > now + 5000 ||
                !Valid(CameraDistanceMeters, 5000) || CameraDistanceMeters < 1 ? 0 : CameraDistanceMeters,
            LaneGuidance = ReadLanes(showLanes, now)
        };
    }

    internal static LaneGuidance ClearLanes() => new(new Lane(LaneDirection.Cancel));

    private LaneGuidance ReadLanes(bool enabled, long now)
    {
        // An empty list is ignored by the host. Send an explicit Cancel lane.
        if (!enabled || !Active || Maneuver == "Rerouting" || Lanes is null ||
            LanesObservedAtUnixMilliseconds < now - 5000 || LanesObservedAtUnixMilliseconds > now + 1000)
            return ClearLanes();
        var items = Lanes.Split(',');
        if (items.Length is < 1 or > 8) return ClearLanes();
        var lanes = new List<Lane>();
        bool recommended = false;
        foreach (var item in items)
        {
            bool selected = item.EndsWith("!", StringComparison.Ordinal);
            var name = selected ? item[..^1] : item;
            LaneDirection? direction = name switch
            {
                "S" => LaneDirection.Straight, "L" => LaneDirection.Left,
                "R" => LaneDirection.Right, "U" => LaneDirection.TurnAroundLeft,
                "SR" => LaneDirection.SlightRight, _ => null
            };
            if (direction is null) return ClearLanes();
            lanes.Add(new Lane(direction.Value, selected));
            recommended |= selected;
        }
        return recommended ? new LaneGuidance(lanes.ToArray()) : ClearLanes();
    }

    private string HudRoadText()
    {
        var road = Active ? Road ?? CurrentRoad : CurrentRoad;
        var parts = new List<string>();
        if (SpeedLimitKph is >= 5 and <= 200) parts.Add($"{SpeedLimitKph} km/h");
        if (RoadEvent is "ДТП" or "Дорожные работы" or "Перекрытие" or "Опасность" or "Лежачий полицейский")
            parts.Add(RoadEvent + (Valid(RoadEventDistanceMeters, 5000) && RoadEventDistanceMeters > 0 ? $" {RoadEventDistanceMeters:0} м" : ""));
        if (!string.IsNullOrWhiteSpace(road)) parts.Add(road.Trim());
        string text = string.Join(" | ", parts);
        // Empty/null is ignored by the installed converter and leaves the old
        // street visible. A literal hyphen explicitly replaces that stale text.
        return text.Length == 0 ? "-" : text[..Math.Min(64, text.Length)];
    }

    private static bool Valid(double number, double max) => double.IsFinite(number) && number >= 0 && number <= max;
}
