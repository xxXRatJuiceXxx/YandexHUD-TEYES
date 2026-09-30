#if DEBUG
using Android.Content;
using Android.Graphics;

namespace YandexNotificationBridge;

internal static class ScreenArrowFixtures
{
    internal static int Run(Context navigator)
    {
        int count = 0;
        var resources = new[] {
            ("context_ra_turn_left", "Left"), ("context_ra_turn_right", "Right"),
            ("context_ra_forward", "Straight"), ("context_ra_finish", "Finish"),
            ("context_ra_take_left", "LeftWide"), ("context_ra_take_right", "RightWide"),
            ("context_ra_hard_turn_left", "LeftSharp"), ("context_ra_hard_turn_right", "RightSharp"),
            ("context_ra_turn_back_left", "UTurnLeft"), ("context_ra_turn_back_right", "UTurnRight"),
            ("directions_left_48", "Left"), ("directions_right_48", "Right"),
            ("directions_straight_48", "Straight"), ("directions_finish_48", "Finish"),
            ("directions_slight_left_48", "LeftWide"), ("directions_slight_right_48", "RightWide"),
            ("directions_hard_left_48", "LeftSharp"), ("directions_hard_right_48", "RightSharp"),
            ("directions_uturn_left_48", "UTurnLeft"), ("directions_uturn_right_48", "UTurnRight")
        };
        foreach (var (resource, expected) in resources)
            foreach (var (background, ink) in new[] { (Color.White, Color.Black), (Color.Rgb(30, 30, 30), Color.White), (Color.Rgb(23, 98, 255), Color.White) })
                foreach (int size in new[] { 64, 150, 224 })
                {
                    int id = navigator.Resources!.GetIdentifier(resource, "drawable", NavigationSource.PackageName);
                    if (id == 0) throw new InvalidOperationException("Missing foreground arrow fixture: " + resource);
                    using var drawable = navigator.GetDrawable(id)!;
                    using var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!)!;
                    using var canvas = new Canvas(bitmap);
                    canvas.DrawColor(background);
                    drawable.SetTint(ink);
                    int padding = size / 7;
                    drawable.SetBounds(padding, padding, size - padding, size - padding);
                    drawable.Draw(canvas);
                    var pixels = new int[size * size];
                    bitmap.GetPixels(pixels, 0, size, 0, 0, size, size);
                    var mask = ScreenArrowMask.Extract(pixels, size, size);
                    var actual = mask is null ? null : ManeuverRecognizer.RecognizeMask(navigator, mask);
                    if (actual != expected) throw new InvalidOperationException($"Foreground arrow {resource}, {size}, {background}: {actual ?? "none"} != {expected}");
                    count++;
                }
        // Real unsupported icons must not accidentally become a known maneuver.
        foreach (string resource in new[] { "context_ra_in_circular_movement", "context_ra_out_circular_movement", "road_alerts_camera_32" })
        {
            int id = navigator.Resources!.GetIdentifier(resource, "drawable", NavigationSource.PackageName);
            using var drawable = navigator.GetDrawable(id)!;
            using var bitmap = Bitmap.CreateBitmap(150, 150, Bitmap.Config.Argb8888!)!;
            using var canvas = new Canvas(bitmap); canvas.DrawColor(Color.Rgb(23, 98, 255));
            drawable.SetTint(Color.White); drawable.SetBounds(20, 20, 130, 130); drawable.Draw(canvas);
            var pixels = new int[150 * 150]; bitmap.GetPixels(pixels, 0, 150, 0, 0, 150, 150);
            var mask = ScreenArrowMask.Extract(pixels, 150, 150);
            if (mask is not null && ManeuverRecognizer.RecognizeMask(navigator, mask) is not null)
                throw new InvalidOperationException("Unsupported foreground icon accepted: " + resource);
            count++;
        }
        return count;
    }
}
#endif
