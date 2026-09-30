using Android.Content;
using Android.OS;

namespace YandexHudHeadUnit;

internal static class ConnectionRecovery
{
    private static long _lastAttempt = -10000;
    private static readonly object Gate = new();
    internal static void TryStart(Context context)
    {
        if (!ReconnectPolicy.ShouldResume(Options.Boot, Options.Enabled, Options.Address, HudService.Running,
                BluetoothAccess.ConnectGranted(context))) return;
        lock (Gate)
        {
            long now = SystemClock.ElapsedRealtime();
            if (now - _lastAttempt < 5000) return;
            _lastAttempt = now;
        }
        try { context.StartForegroundService(new Intent(context, typeof(HudService))); }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("YandexHUD", "Autostart: " + ex.GetType().Name);
            HudService.SetStatus("Android отложил автозапуск. Откройте приложение для подключения к HUD.");
        }
    }
}
