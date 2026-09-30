using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views.Accessibility;

namespace YandexNotificationBridge;

#if HEADUNIT
[Service(Name = "com.avashield.yandexhud.headunit.YandexScreenService", Label = "Яндекс HUD — магнитола: чтение навигации",
#else
[Service(Name = "com.avashield.yandexnotificationbridge.YandexScreenService", Label = "Яндекс → HUD: чтение навигации",
#endif
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/yandex_screen_service")]
public sealed class YandexScreenService : AccessibilityService
{
    private HandlerThread? _thread;
    private Handler? _worker;
    private volatile bool _running;
    internal static bool Connected { get; private set; }
    internal static BridgeState? Current => Volatile.Read(ref _current);
    private static BridgeState? _current;
    private IReadOnlyDictionary<string, string>? _labels;
#if HEADUNIT
    private readonly ForegroundArrowReader _visual = new();
#endif
    internal static string? ManeuverDescription { get; private set; }
    internal static long ManeuverDescriptionAt { get; private set; }
#if DEBUG && HEADUNIT
    // Reproduce an image-only legacy balloon on the connected modern Navigator.
    internal static bool IgnoreDescriptionsForProbe;
#endif
    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        Connected = true;
        _labels = ManeuverLabels.Read(this);
#if HEADUNIT
        YandexHudHeadUnit.ConnectionRecovery.TryStart(this);
#endif
        BridgeHub.Start();
        _thread = new HandlerThread("YandexScreenReader");
        _thread.Start();
        _worker = new Handler(_thread.Looper!);
        _running = true;
        _worker.Post(Poll);
    }
    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
#if HEADUNIT
        if (NavigationSource.AcceptsPackage(e?.PackageName?.ToString())) YandexHudHeadUnit.ConnectionRecovery.TryStart(this);
#endif
        // Poll complete snapshots rather than potentially partial event text.
    }
    public override void OnInterrupt() => Volatile.Write(ref _current, null);
    public override void OnDestroy()
    {
        _running = false; Connected = false;
        _worker?.RemoveCallbacksAndMessages(null);
        _thread?.QuitSafely();
        Volatile.Write(ref _current, null);
        base.OnDestroy();
    }
    private async void Poll()
    {
        if (!_running) return;
        try
        {
            // Binding queries can block in another app. Keep them off the main
            // looper and socket request path. Stale samples expire in BridgeHub.
            using var root = RootInActiveWindow;
            if (root is null || !NavigationSource.AcceptsPackage(root.PackageName?.ToString()))
            { Volatile.Write(ref _current, null); }
            else
            {
                var fields = new List<ScreenField>();
                int budget = 650;
                Visit(root, fields, ref budget, 0);
                ManeuverDescription = fields.Where(f => ScreenNavigationReader.IsCurrentArrow(f.Id))
                    .Select(f => f.Description ?? f.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                ManeuverDescriptionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var state = ScreenNavigationReader.Read(fields, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _labels);
#if HEADUNIT
                if (YandexHudHeadUnit.Options.VisualArrows && state.Active && state.Maneuver is null &&
                    ScreenNavigationReader.ReadTurnDistance(fields) is not null && ScreenArrowGuard.Region(fields) is { } area)
                {
                    long started = SystemClock.UptimeMillis();
                    var direction = await _visual.ReadAsync(this, _worker!, root.WindowId, area);
                    // A capture can finish after a maneuver, window, rotation or
                    // route has changed. Re-read before applying any image result.
                    using var currentRoot = RootInActiveWindow;
                    if (!_running) return;
                    if (currentRoot is null || !NavigationSource.AcceptsPackage(currentRoot.PackageName?.ToString()))
                        Volatile.Write(ref _current, null);
                    else
                    {
                        var currentFields = new List<ScreenField>(); int currentBudget = 650;
                        Visit(currentRoot, currentFields, ref currentBudget, 0);
                        var currentState = ScreenNavigationReader.Read(currentFields, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _labels);
                        if (direction is not null && YandexHudHeadUnit.Options.VisualArrows && currentState.Maneuver is null &&
                            ScreenNavigationReader.ReadTurnDistance(currentFields) is not null &&
                            ScreenArrowGuard.SameFrame(state, currentState, area, ScreenArrowGuard.Region(currentFields),
                                root.WindowId, currentRoot.WindowId, SystemClock.UptimeMillis() - started))
                            currentState = currentState with { Maneuver = direction };
                        NavigationDiagnostics.RecordScreen(currentFields, currentState);
                        Volatile.Write(ref _current, currentState);
                    }
                    if (_running) _worker?.PostDelayed(Poll, 500);
                    return;
                }
#endif
                NavigationDiagnostics.RecordScreen(fields, state);
                Volatile.Write(ref _current, state);
            }
        }
        catch (Exception) { Volatile.Write(ref _current, null); }
        if (_running) _worker?.PostDelayed(Poll, 500);
    }
    private static void Visit(AccessibilityNodeInfo node, List<ScreenField> fields, ref int budget, int depth)
    {
        if (--budget < 0 || depth > 40) return;
        var id = node.ViewIdResourceName;
        const string prefix = NavigationSource.ResourcePrefix;
        if (node.VisibleToUser && id?.StartsWith(prefix, StringComparison.Ordinal) == true)
        {
            id = id[prefix.Length..];
            if (ScreenNavigationReader.FieldIds.Contains(id))
            {
                ScreenArea? area = null;
                if (ScreenNavigationReader.IsCurrentArrow(id))
                {
                    using var bounds = new Android.Graphics.Rect(); node.GetBoundsInScreen(bounds);
                    area = new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                }
                string? text = node.Text, description = node.ContentDescription;
#if DEBUG && HEADUNIT
                if (IgnoreDescriptionsForProbe && ScreenNavigationReader.IsCurrentArrow(id)) text = description = null;
#endif
                fields.Add(new(id, text, description, area));
            }
        }
        for (int i = 0; i < node.ChildCount && budget > 0; i++)
        {
            using var child = node.GetChild(i);
            if (child is not null) Visit(child, fields, ref budget, depth + 1);
        }
    }
}

