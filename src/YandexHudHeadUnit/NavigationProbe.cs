#if DEBUG
using Android.Content;
using System.Text.Json;
using YandexNotificationBridge;

namespace YandexHudHeadUnit;

// Explicit ADB test only. Excluded from the distributable Release APK.
internal static class NavigationProbe
{
    internal static async Task RunAsync(Context context)
    {
        var path = Path.Combine(context.CacheDir!.AbsolutePath, "navigation-probe.jsonl");
        try
        {
            using var output = new StreamWriter(path, false);
            for (int i = 0; i < 25; i++)
            {
                var state = await BridgeHub.ReadAsync();
                var sample = JsonSerializer.SerializeToNode(state)!;
                sample["HudDirection"] = HudProtocol.Direction(HudProtocol.Map(state, true).NextTurnDirection);
                sample["ManeuverDescription"] = YandexScreenService.ManeuverDescription;
                await output.WriteLineAsync(sample.ToJsonString());
                await output.FlushAsync();
                await Task.Delay(1000);
            }
            Android.Util.Log.Info("YandexHeadunitProbe", "Completed local navigation probe");
        }
        catch (Exception ex) { Android.Util.Log.Error("YandexHeadunitProbe", ex.ToString()); }
    }
}
#endif
