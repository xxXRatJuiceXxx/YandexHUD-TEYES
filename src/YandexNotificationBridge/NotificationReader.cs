using System.Globalization;
using System.Text.RegularExpressions;
using Android.App;
using Android.Content;
using Android.Service.Notification;
using Android.Views;
using Android.Widget;

namespace YandexNotificationBridge;

internal static class NotificationReader
{
    private static readonly Regex Distance = new(@"^\s*(?<number>\d{1,3}(?:[ \u00a0\u202f]\d{3})+|\d+)(?:[.,](?<decimal>\d+))?\s*(?<unit>км|м|km|m)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Hours = new(@"(?<n>\d+)\s*(?:ч\b|час\p{L}*|h\b|hours?\b)", RegexOptions.IgnoreCase);
    private static readonly Regex Minutes = new(@"(?<n>\d+)\s*(?:мин\p{L}*|min\p{L}*)", RegexOptions.IgnoreCase);

    public static BridgeState Read(Context context, StatusBarNotification sbn)
        => NavigationSource.AcceptsPackage(sbn.PackageName) && sbn.Notification is { } notification
            ? Read(context, notification, sbn.PostTime) : BridgeState.Inactive;

    internal static BridgeState Read(Context context, Notification notification, long postTime)
    {
        using var yandex = context.CreatePackageContext(NavigationSource.PackageName, PackageContextFlags.IgnoreSecurity);
        if (yandex is null) return BridgeState.Inactive;
        BridgeState? best = null;
        var layouts = new List<string>();
        double remainingDistance = 0, seconds = 0;
        string? lanes = null;
        foreach (var remote in new[] { notification.BigContentView, notification.ContentView, notification.HeadsUpContentView })
        {
            if (remote is null) continue;
            string layout = "0x" + remote.LayoutId.ToString("X");
            try
            {
                layout = yandex.Resources?.GetResourceEntryName(remote.LayoutId) ?? layout;
                using var parent = new FrameLayout(yandex);
                using var root = remote.Apply(yandex, parent);
                if (root is null) continue;
                try { lanes ??= ReadLanes(root, yandex); }
                catch (Exception) { /* A lane bitmap cannot suppress the maneuver/road. */ }
                string? title = Find<TextView>(root, yandex, "titleView")?.Text;
                string? road = Find<TextView>(root, yandex, "descriptionView")?.Text;
                string? remaining = Find<TextView>(root, yandex, "remainingDistanceView")?.Text;
                string? duration = Find<TextView>(root, yandex, "remainingTimeView")?.Text;
                if (title is null && remaining is null && duration is null) continue;
                var distance = ParseDistance(title);
                remainingDistance = ParseDistance(remaining) ?? remainingDistance;
                if (duration is not null) seconds = ParseDuration(duration);
                // Maps 30.7.3 BackgroundGuidancePanelCameraData: distance is titleView,
                // notification_speed_camera is descriptionView, primaryIcon is NOT tinted.
                // It replaces the maneuver. Never reuse a hidden/previous turn image.
                if ((Find<ImageView>(root, yandex, "primaryIcon") ?? Find<ImageView>(root, yandex, "nextManeuverView")) is not null &&
                    CameraText.IsCameraDescription(road, ReadString(yandex, "notification_speed_camera")))
                {
                    layouts.Add(layout + ": камера");
                    return NavigationDiagnostics.RecordNotification(new(true, "Camera", 0, null, remainingDistance, seconds, 0, 0, 0,
                        CameraDistanceMeters: CameraText.ApproachingDistance(title), CameraObservedAtUnixMilliseconds: postTime), layouts, postTime);
                }
                // Both layouts still exist in Navigator. The classic layout
                // keeps titleView/descriptionView but names its current arrow
                // nextManeuverViewTinted. It is not the second turn in a route.
                var image = Find<ImageView>(root, yandex, "primaryIconTinted") ?? Find<ImageView>(root, yandex, "nextManeuverViewTinted");
                var instruction = image?.Drawable is { } drawable ? ManeuverRecognizer.Recognize(yandex, drawable) : null;
                layouts.Add(layout + ": значок=" + (image is null ? "нет" : yandex.Resources?.GetResourceEntryName(image.Id)) +
                    "; поворот=" + (instruction ?? "нет") + "; расстояние=" + (distance?.ToString(CultureInfo.InvariantCulture) ?? "не прочитано"));
                var status = (title + " " + road).ToLowerInvariant();
                if (status.Contains("перестро") || status.Contains("rerouting") || status.Contains("recalculating"))
                    return NavigationDiagnostics.RecordNotification(new(true, "Rerouting", 0, null, remainingDistance, seconds, 0, 0, 0), layouts, postTime);
                // Expanded lanes can combine "distance · road" and omit the
                // maneuver image. Continue to the compact layout, retaining ETA.
                if (distance is null) continue;
                if (best is null || best.Maneuver is null && instruction is not null)
                    best = new(true, instruction, distance.Value, road, remainingDistance, seconds, 0, 0, 0);
            }
            catch (Exception ex) { layouts.Add(layout + ": ошибка " + ex.GetType().Name); /* Try the other layout. */ }
        }
        return NavigationDiagnostics.RecordNotification(best is null ? BridgeState.Inactive : best with { RemainingDistanceMeters = remainingDistance, RemainingTimeSeconds = seconds,
            Lanes = lanes, LanesObservedAtUnixMilliseconds = lanes is null ? 0 : postTime }, layouts, postTime);
    }

    private static string? ReadLanes(View root, Context maps)
    {
        var items = new List<string>();
        bool gap = false;
        for (int i = 1; i <= 8; i++)
        {
            var view = Find<ImageView>(root, maps, "lane" + i);
            if (view is null) { gap = true; continue; }
            if (gap) return null;
            if (view.Drawable is not { } drawable || LaneRecognizer.Recognize(maps, drawable) is not { } lane) return null;
            items.Add(lane);
        }
        return items.Count > 0 && items.Any(l => l.EndsWith("!", StringComparison.Ordinal)) ? string.Join(",", items) : null;
    }

    private static T? Find<T>(View root, Context context, string name) where T : View
    {
        if (root.Visibility != ViewStates.Visible) return null;
        if (root is T target && root.Id > 0)
        {
            try { if (context.Resources?.GetResourceEntryName(root.Id) == name) return target; }
            catch (Exception) { }
        }
        if (root is ViewGroup group)
            for (int i = 0; i < group.ChildCount; i++)
                if (group.GetChildAt(i) is { } child && Find<T>(child, context, name) is { } found) return found;
        return null;
    }

    private static string? ReadString(Context context, string name)
    {
        int id = context.Resources?.GetIdentifier(name, "string", NavigationSource.PackageName) ?? 0;
        return id == 0 ? null : context.GetString(id);
    }

    internal static double? ParseDistance(string? text)
    {
        var match = Distance.Match(text ?? "");
        if (!match.Success) return null;
        var value = match.Groups["number"].Value.Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", "");
        if (match.Groups["decimal"].Success) value += "." + match.Groups["decimal"].Value;
        if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var distance)) return null;
        if (match.Groups["unit"].Value.Length == 2) distance *= 1000;
        return distance <= 50_000_000 && double.IsFinite(distance) ? distance : null;
    }

    private static double ParseDuration(string? text)
    {
        var h = Hours.Match(text ?? "");
        var m = Minutes.Match(text ?? "");
        double hours = h.Success && double.TryParse(h.Groups["n"].Value, out var hv) ? hv : 0;
        double minutes = m.Success && double.TryParse(m.Groups["n"].Value, out var mv) ? mv : 0;
        return Math.Min(hours * 3600 + minutes * 60, 3_000_000);
    }
}
