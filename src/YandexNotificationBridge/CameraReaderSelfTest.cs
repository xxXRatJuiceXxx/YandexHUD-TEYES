#if DEBUG
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace YandexNotificationBridge;

// Android fixtures using the installed Maps layouts and resources, not a live
// road test. They never post notifications, alter bridge state or contact a HUD.
internal static class CameraReaderSelfTest
{
    internal static void Run(Context context)
    {
        try
        {
            using var maps = context.CreatePackageContext(NavigationSource.PackageName, PackageContextFlags.IgnoreSecurity)!;
            int Id(string name, string type = "id") => maps.Resources!.GetIdentifier(name, type, NavigationSource.PackageName);
            var label = maps.GetString(Id("notification_speed_camera", "string"));
            int count = LegacyNavigationFixtures.Run(context);
            count += ScreenArrowFixtures.Run(maps);
            var installedLabels = ManeuverLabels.Read(context);
            foreach (var sample in installedLabels)
            {
                if (ScreenNavigationReader.ReadManeuver(sample.Key, installedLabels) != sample.Value)
                    throw new InvalidOperationException("Installed maneuver description not recognized: " + sample.Key);
                count++;
            }
            foreach (string layout in new[] { "redesign_bg_guidance_notification_flat", "redesign_bg_guidance_notification" })
            {
                foreach (var sample in new[] { ("350 м", 350d), ("1,2 км", 1200d), ("−100 м", 0d), ("0 м", 0d), ("60 км/ч", 0d) })
                {
                    using var remote = new RemoteViews(NavigationSource.PackageName, Id(layout, "layout"));
                    remote.SetTextViewText(Id("titleView"), sample.Item1);
                    remote.SetTextViewText(Id("descriptionView"), label);
                    remote.SetViewVisibility(Id("descriptionView"), ViewStates.Visible);
                    remote.SetImageViewResource(Id("primaryIcon"), Id("road_alerts_camera_32", "drawable"));
                    remote.SetViewVisibility(Id("primaryIcon"), ViewStates.Visible);
                    // A stale hidden turn icon must never masquerade as a live turn.
                    remote.SetImageViewResource(Id("primaryIconTinted"), Id("directions_right_s_32", "drawable"));
                    remote.SetViewVisibility(Id("primaryIconTinted"), ViewStates.Gone);
                    using var notification = new Notification { BigContentView = remote };
                    var state = NotificationReader.Read(context, notification, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    if (!state.Active || state.Maneuver != "Camera" || state.NextTurnDistanceMeters != 0 || state.Road != null || state.CameraDistanceMeters != sample.Item2)
                        throw new InvalidOperationException($"Camera fixture failed: {layout}/{sample.Item1}: {state}");
                    count++;
                    remote.SetViewVisibility(Id("descriptionView"), ViewStates.Gone);
                    state = NotificationReader.Read(context, notification, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    if (state.CameraDistanceMeters != 0 || state.Maneuver == "Camera")
                        throw new InvalidOperationException("Hidden camera description was read");
                    count++;
                }
            }
            using var expanded = new RemoteViews(NavigationSource.PackageName, Id("redesign_bg_guidance_notification_multiple", "layout"));
            expanded.SetTextViewText(Id("titleView"), "300 м · Тестовая улица");
            expanded.SetTextViewText(Id("remainingDistanceView"), "5 км");
            expanded.SetTextViewText(Id("remainingTimeView"), "13 мин");
            expanded.SetViewVisibility(Id("remainingDistanceView"), ViewStates.Visible);
            expanded.SetViewVisibility(Id("remainingTimeView"), ViewStates.Visible);
            using var compact = new RemoteViews(NavigationSource.PackageName, Id("redesign_bg_guidance_notification", "layout"));
            compact.SetTextViewText(Id("titleView"), "300 м");
            compact.SetTextViewText(Id("descriptionView"), "Тестовая улица");
            compact.SetViewVisibility(Id("descriptionView"), ViewStates.Visible);
            compact.SetImageViewResource(Id("primaryIconTinted"), Id("directions_right_s_32", "drawable"));
            compact.SetViewVisibility(Id("primaryIconTinted"), ViewStates.Visible);
            compact.SetViewVisibility(Id("primaryIcon"), ViewStates.Gone);
            using var multi = new Notification { BigContentView = expanded, ContentView = compact };
            var combined = NotificationReader.Read(context, multi, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (combined.Maneuver != "Right" || combined.NextTurnDistanceMeters != 300 || combined.RemainingDistanceMeters != 5000 || combined.RemainingTimeSeconds != 780)
                throw new InvalidOperationException("Expanded lanes prevented compact navigation parsing: " + combined);
            count++;
            foreach (var direction in new[] { ("left", "UTurnLeft"), ("right", "UTurnRight") })
            {
                string description = maps.GetString(Id("navi_make_a_" + direction.Item1 + "_u_turn", "string"));
                if (ScreenNavigationReader.ReadManeuver(description) != direction.Item2)
                    throw new InvalidOperationException("Installed Maps U-turn label not recognized: " + description);
                count++;
                compact.SetImageViewResource(Id("primaryIconTinted"), Id("directions_uturn_" + direction.Item1 + "_s_32", "drawable"));
                var turn = NotificationReader.Read(context, multi, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (turn.Maneuver != direction.Item2 || turn.NextTurnDistanceMeters != 300)
                    throw new InvalidOperationException("Installed Maps U-turn image not recognized: " + turn);
                count++;
            }
            foreach (var lane in new[] { ("left90", "L"), ("straightahead", "S"), ("right90", "R"), ("left180", "U"), ("right45", "SR") })
            {
                foreach (bool selected in new[] { true, false })
                {
                    using var drawable = maps.GetDrawable(Id("context_lane_" + lane.Item1 + "_large", "drawable"))!;
                    drawable.Alpha = selected ? 255 : 102;
                    string? recognized = LaneRecognizer.Recognize(maps, drawable);
                    if (recognized != lane.Item2 + (selected ? "!" : ""))
                        throw new InvalidOperationException($"Lane fixture failed: {lane}/{selected}: {recognized}");
                    count++;
                }
            }
            using var unsupported = maps.GetDrawable(Id("context_lane_bus_ru", "drawable"))!;
            if (LaneRecognizer.Recognize(maps, unsupported) is not null)
                throw new InvalidOperationException("Bus lane must not be substituted with a direction");
            count++;
            // The notification uses one bitmap per lane, dimming secondary
            // directions to 40% opacity. These are fixtures, never live warnings.
            foreach (var lane in new[] { (1, "left90", true), (2, "straightahead", false), (3, "right90", false) })
            {
                using var bitmap = Bitmap.CreateBitmap(132, 132, Bitmap.Config.Argb8888!)!;
                using var canvas = new Canvas(bitmap);
                using var drawable = maps.GetDrawable(Id("context_lane_" + lane.Item2 + "_large", "drawable"))!;
                drawable.SetBounds(0, 0, 132, 132);
                drawable.Alpha = lane.Item3 ? 255 : 102;
                drawable.Draw(canvas);
                expanded.SetImageViewBitmap(Id("lane" + lane.Item1), bitmap);
                expanded.SetViewVisibility(Id("lane" + lane.Item1), ViewStates.Visible);
            }
            for (int i = 4; i <= 8; i++) expanded.SetViewVisibility(Id("lane" + i), ViewStates.Gone);
            expanded.SetViewVisibility(Id("lanesContainer"), ViewStates.Visible);
            long postTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var lanesState = NotificationReader.Read(context, multi, postTime);
            if (lanesState.Lanes != "L!,S,R" || lanesState.LanesObservedAtUnixMilliseconds != postTime)
                throw new InvalidOperationException("RemoteViews lane row failed: " + lanesState);
            count++;
            foreach (var turn in new[] { ("right", "Right"), ("left", "Left"), ("straight", "Straight"),
                ("slight_right", "RightWide"), ("slight_left", "LeftWide"),
                ("hard_right", "RightSharp"), ("hard_left", "LeftSharp"),
                ("fork_right", "RightWide"), ("fork_left", "LeftWide"),
                ("uturn_right", "UTurnRight"), ("uturn_left", "UTurnLeft"), ("finish", "Finish") })
            {
                foreach (string resource in new[] { "directions_" + turn.Item1 + "_48", "notification_" + turn.Item1 + "_sdl" })
                {
                compact.SetImageViewResource(Id("primaryIconTinted"), Id(resource, "drawable"));
                var recognized = NotificationReader.Read(context, multi, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (recognized.Maneuver != turn.Item2)
                    throw new InvalidOperationException("Maneuver variant failed: " + resource + ": " + recognized.Maneuver);
                count++;
                }
            }
            Android.Util.Log.Info("YandexCameraTest", $"PASS: {count} Android RemoteViews camera/navigation fixtures; no live data changed");
        }
        catch (Exception ex) { Android.Util.Log.Error("YandexCameraTest", "FAIL: " + ex); }
    }
}
#endif

