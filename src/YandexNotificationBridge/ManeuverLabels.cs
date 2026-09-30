using Android.Content;

namespace YandexNotificationBridge;

internal static class ManeuverLabels
{
    // Resolve descriptions from the installed Navigator/Maps version, including
    // its current locale. Resource names have a fixed semantic mapping.
    internal static IReadOnlyDictionary<string, string> Read(Context context)
    {
        var labels = new Dictionary<string, string>();
        try
        {
            using var source = context.CreatePackageContext(NavigationSource.PackageName, PackageContextFlags.IgnoreSecurity);
            if (source is null) return labels;
            foreach (var (name, direction) in new (string, string)[] {
                ("navi_turn_right", "Right"), ("navi_turn_left", "Left"),
                ("navi_make_a_slight_right", "RightWide"), ("navi_make_a_slight_left", "LeftWide"),
                ("navi_make_a_left_u_turn", "UTurnLeft"), ("navi_make_a_right_u_turn", "UTurnRight"),
                ("accessibility_maneuver_hard_turn_left", "LeftSharp"), ("accessibility_maneuver_hard_turn_right", "RightSharp") })
            {
                int id = source.Resources?.GetIdentifier(name, "string", NavigationSource.PackageName) ?? 0;
                if (id == 0) continue;
                var text = ScreenNavigationReader.NormalizeManeuver(source.GetString(id));
                if (text.Length > 0) labels[text] = direction;
            }
        }
        catch (Exception) { /* Known literal descriptions still work. */ }
        return labels;
    }
}
