using WiiYiiHudNavigator.Common.Hud;
using WiiYiiHudNavigator.Common.Models;

namespace WiiYiiHudNavigator.Plugin.Yandex;

// A slow Bluetooth write must not block source polling, stop/restart buttons or
// accumulate obsolete route updates. Only one write runs; only the newest state
// waits behind it. A clear published on stop replaces every queued maneuver.
internal sealed class HudOutputPump
{
    private readonly IHudConnection _hud;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private HudNavigationData? _latest;
    private bool _signaled;
    private long _sendingSince;
    private string? _error;
    internal HudOutputPump(IHudConnection hud) { _hud = hud; _ = RunAsync(); }
    internal string? Error { get { lock (_gate) return _error; } }
    internal bool Delayed
    {
        get { lock (_gate) return _sendingSince != 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _sendingSince > 4000; }
    }
    internal void Publish(HudNavigationData data)
    {
        lock (_gate)
        {
            _latest = data;
            // CurrentCount and Volatile are trimmed out of the installed host.
            if (!_signaled) { _signaled = true; _signal.Release(); }
        }
    }
    private async Task RunAsync()
    {
        while (true)
        {
            await _signal.WaitAsync();
            HudNavigationData? data;
            lock (_gate) { _signaled = false; data = _latest; _latest = null; }
            if (data is null) continue;
            lock (_gate) _sendingSince = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                await _hud.UpdateNavigationData(data);
                lock (_gate) _error = null;
            }
            catch (Exception ex) { lock (_gate) _error = ex.Message; }
            finally { lock (_gate) _sendingSince = 0; }
        }
    }
}
