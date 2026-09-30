using System.Text;
using WiiYiiHudNavigator.Common.Models;
using WiiYiiHudNavigator.Plugin.Yandex;
using SourceState = YandexNotificationBridge.BridgeState;

namespace YandexHudHeadUnit;

// Interoperable C1/WYHUD wire frames. UUIDs and framing match WiiYii 0.15.
// Only the commands already used by the working phone integration are sent.
internal static class HudProtocol
{
    internal const string ServiceUuid = "0000fee9-0000-1000-8000-00805f9b34fb";
    internal const string WriteUuid = "d44bc439-abfd-45a2-b575-925416129600";

    internal static HudNavigationData Map(SourceState s, bool lanes) => new BridgeState(
        s.Active, s.Maneuver, s.NextTurnDistanceMeters, s.Road, s.RemainingDistanceMeters,
        s.RemainingTimeSeconds, s.UpdatedAtUnixMilliseconds, s.Sequence, s.ObservedAtUnixMilliseconds,
        s.ProtocolVersion, s.CameraDistanceMeters, s.CameraObservedAtUnixMilliseconds, s.Source,
        s.Mode, s.CurrentRoad, s.SpeedLimitKph, s.RoadEvent, s.RoadEventDistanceMeters,
        s.ScreenReaderConnected, s.SourceTimestamp, s.Lanes, s.LanesObservedAtUnixMilliseconds)
        .ToHudData(latinText: true, showLanes: lanes) ?? Clear();

    internal static HudNavigationData Clear() => new()
    {
        NextTurnDirection = (DirectionInstruction)(-1), NextTurnRoadName = "-",
        CameraDistance = 0, LaneGuidance = BridgeState.ClearLanes()
    };

    internal static byte[][] Frames(HudNavigationData data)
    {
        var direction = Direction(data.NextTurnDirection);
        uint distance = (uint)FiniteClamp(data.NextTurnDistance, 2_000_000);
        var next = data.SecondTurnDirection is { } second ? Direction(second) : direction;
        int minutes = (int)Math.Round(FiniteClamp(data.EtaDurationTimeRemaining / 60, 15359), MidpointRounding.AwayFromZero);
        int remaining = (int)Math.Round(FiniteClamp(data.EtaDistanceRemaining / 100, 65535), MidpointRounding.AwayFromZero);
        int camera = (int)FiniteClamp(data.CameraDistance ?? 0, 65535);
        byte[] road = Encoding.ASCII.GetBytes(HudText.Encode(data.NextTurnRoadName));
        return [
            Frame(1, [(byte)(direction >> 8), (byte)direction, (byte)(distance >> 8), (byte)distance,
                1, (byte)(distance >> 24), (byte)(distance >> 16), (byte)(next >> 8), (byte)next]),
            Frame(9, [(byte)(minutes / 60), (byte)(minutes % 60), (byte)(remaining >> 8), (byte)remaining]),
            Frame(6, [(byte)road.Length, ..road]),
            Frame(3, [(byte)(camera > 0 ? 1 : 0), (byte)(camera >> 8), (byte)camera]),
            Frame(4, (data.LaneGuidance is { Lanes.Count: > 0 } lanes ? lanes : BridgeState.ClearLanes()).ToByteArray())
        ];
    }

    private static double FiniteClamp(double value, double max) => double.IsFinite(value) ? Math.Clamp(value, 0, max) : 0;
    internal static ushort Direction(DirectionInstruction? direction) => direction switch
    {
        DirectionInstruction.Rerouting => ushort.MaxValue,
        DirectionInstruction.Right => 3,
        DirectionInstruction.RightWide or DirectionInstruction.RightFork => 5,
        DirectionInstruction.RightSharpSE => 7,
        DirectionInstruction.UTurnRight => 19,
        DirectionInstruction.Left => 2,
        DirectionInstruction.LeftWide or DirectionInstruction.LeftFork => 4,
        DirectionInstruction.LeftSharpSE => 6,
        DirectionInstruction.UTurnLeft => 8,
        DirectionInstruction.Straight => 9,
        DirectionInstruction.Finish => 15,
        _ => 0
    };

    private static byte[] Frame(byte command, byte[] payload)
    {
        int checksum = 104 + command + payload.Sum(b => (int)b);
        byte[] message = [49, 48, (byte)(checksum >> 8), (byte)checksum, 7, command, ..payload];
        return Encoding.ASCII.GetBytes("^" + Convert.ToBase64String(message) + "$");
    }
}
