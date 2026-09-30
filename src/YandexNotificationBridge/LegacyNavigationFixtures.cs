#if DEBUG
using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;

namespace YandexNotificationBridge;

internal static class LegacyNavigationFixtures
{
    internal static int Run(Context context)
    {
        using var source = context.CreatePackageContext(NavigationSource.PackageName, PackageContextFlags.IgnoreSecurity)!;
        int Id(string name, string type = "id") => source.Resources!.GetIdentifier(name, type, NavigationSource.PackageName);
        int count = 0;
        foreach (var layout in new[] { "navi_layout_bg_guidance_notification_maneuver_block", "navi_layout_bg_guidance_notification",
            "navi_layout_bg_guidance_notification_pre_android_12", "navi_layout_bg_guidance_notification_large",
            "navi_layout_bg_guidance_notification_large_pre_android_12", "navi_layout_bg_guidance_notification_heads_up",
            "navi_layout_bg_guidance_notification_heads_up_pre_android_12" })
        {
            using var remote = new RemoteViews(NavigationSource.PackageName, Id(layout, "layout"));
            remote.SetTextViewText(Id("titleView"), "300 м");
            remote.SetTextViewText(Id("descriptionView"), "Тестовая улица");
            remote.SetViewVisibility(Id("descriptionView"), ViewStates.Visible);
            remote.SetImageViewResource(Id("nextManeuverViewTinted"), Id("directions_right_s_32", "drawable"));
            remote.SetViewVisibility(Id("nextManeuverViewTinted"), ViewStates.Visible);
            remote.SetViewVisibility(Id("nextManeuverView"), ViewStates.Gone);
            using var notification = new Notification { ContentView = remote };
            var state = NotificationReader.Read(context, notification, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (state.Maneuver != "Right" || state.NextTurnDistanceMeters != 300 || state.Road != "Тестовая улица")
                throw new InvalidOperationException("Legacy Navigator layout lost its arrow while keeping the street/distance: " + layout + ": " + state);
            count++;
            foreach (var (resource, expected) in new (string, string)[] {
                ("context_ra_turn_left", "Left"), ("context_ra_turn_right", "Right"),
                ("context_ra_forward", "Straight"), ("context_ra_take_left", "LeftWide"), ("context_ra_take_right", "RightWide"),
                ("context_ra_hard_turn_left", "LeftSharp"), ("context_ra_hard_turn_right", "RightSharp"),
                ("context_ra_turn_back_left", "UTurnLeft"), ("context_ra_turn_back_right", "UTurnRight"), ("context_ra_finish", "Finish") })
            {
                remote.SetImageViewResource(Id("nextManeuverViewTinted"), Id(resource, "drawable"));
                state = NotificationReader.Read(context, notification, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (state.Maneuver != expected || state.NextTurnDistanceMeters != 300)
                    throw new InvalidOperationException("Classic arrow failed: " + layout + "/" + resource + ": " + state.Maneuver);
#if HEADUNIT
                state = state with { ObservedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                var mapped = YandexHudHeadUnit.HudProtocol.Map(state, true);
                if (YandexHudHeadUnit.HudProtocol.Direction(mapped.NextTurnDirection) == 0 || mapped.NextTurnDistance != 300)
                    throw new InvalidOperationException("Classic arrow was lost before HUD output");
#endif
                count++;
            }
            remote.SetTextViewText(Id("descriptionView"), source.GetString(Id("notification_speed_camera", "string")));
            remote.SetTextViewText(Id("titleView"), "350 м");
            remote.SetImageViewResource(Id("nextManeuverView"), Id("road_alerts_camera_32", "drawable"));
            remote.SetViewVisibility(Id("nextManeuverView"), ViewStates.Visible);
            remote.SetViewVisibility(Id("nextManeuverViewTinted"), ViewStates.Gone);
            state = NotificationReader.Read(context, notification, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (state.Maneuver != "Camera" || state.CameraDistanceMeters != 350 || state.NextTurnDistanceMeters != 0)
                throw new InvalidOperationException("Legacy camera must not reuse hidden maneuver: " + layout);
            count++;
            remote.SetViewVisibility(Id("descriptionView"), ViewStates.Gone);
            state = NotificationReader.Read(context, notification, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (state.Maneuver is not null || state.CameraDistanceMeters != 0)
                throw new InvalidOperationException("Hidden classic camera/arrow must not be read: " + layout);
            count++;
        }
        return count;
    }
}
#endif
