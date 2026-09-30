using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;

namespace YandexNotificationBridge;

internal static class ManeuverRecognizer
{
    private static readonly (string Resource, string Direction)[] Candidates =
    [
        ("directions_right_s_32", "Right"), ("directions_left_s_32", "Left"),
        ("directions_straight_s_32", "Straight"),
        ("directions_slight_right_s_32", "RightWide"), ("directions_slight_left_s_32", "LeftWide"),
        ("directions_hard_right_s_32", "RightSharp"), ("directions_hard_left_s_32", "LeftSharp"),
        ("directions_fork_right_s_32", "RightFork"), ("directions_fork_left_s_32", "LeftFork"),
        ("directions_uturn_right_s_32", "UTurnRight"), ("directions_uturn_left_s_32", "UTurnLeft"),
        ("directions_finish_s_32", "Finish")
    ];
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool[]> Templates = new();

    public static string? Recognize(Context context, Drawable drawable)
    {
        var live = Mask(drawable);
        if (live is null) return null;
        return RecognizeMask(context, live);
    }

    internal static string? RecognizeMask(Context context, bool[] live)
    {
        var scores = new Dictionary<string, double>();
        foreach (var candidate in Candidates)
        {
            double score = 0;
            // Maps uses both compact and large drawings. Their stroke geometry
            // differs even after resizing. Compare variants within one direction;
            // they must not count as competing runner-up directions.
            foreach (string resource in new[] { candidate.Resource,
                candidate.Resource.Replace("_s_32", "_48"), candidate.Resource.Replace("_s_32", "_40"),
                candidate.Resource.Replace("directions_", "notification_").Replace("_s_32", "_sdl"),
                LegacyResource(candidate.Direction) }.Where(r => r.Length > 0))
            {
                if (!Templates.TryGetValue(resource, out var template))
                {
                    int id = context.Resources?.GetIdentifier(resource, "drawable", NavigationSource.PackageName) ?? 0;
                    if (id == 0) continue;
                    using var reference = context.GetDrawable(id);
                    if (reference is null || Mask(reference) is not { } mask) continue;
                    template = mask;
                    Templates[resource] = template;
                }
                int intersection = 0, union = 0;
                for (int i = 0; i < live.Length; i++)
                {
                    if (live[i] && template[i]) intersection++;
                    if (live[i] || template[i]) union++;
                }
                score = Math.Max(score, union == 0 ? 0 : (double)intersection / union);
            }
            // SDL slight/fork drawings can be identical. Both map to the same
            // HUD arrow, so ambiguity between these labels is not a conflict.
            string code = candidate.Direction switch { "RightFork" => "RightWide", "LeftFork" => "LeftWide", _ => candidate.Direction };
            scores[code] = Math.Max(scores.GetValueOrDefault(code), score);
        }
        var ranked = scores.OrderByDescending(p => p.Value).ToArray();
        double best = ranked[0].Value, runnerUp = ranked[1].Value;
        string direction = ranked[0].Key;
        // Compare only the occupied shape, not the empty background. Ambiguous or
        // unsupported icons (including circle geometry) must not become random turns.
        Android.Util.Log.Info("YandexBridge", $"SHAPE best={direction} iou={best:0.000} margin={best - runnerUp:0.000}");
        return best >= .70 && best - runnerUp >= .15 ? direction : null;
    }

    private static string LegacyResource(string direction) => direction switch
    {
        "Right" => "context_ra_turn_right", "Left" => "context_ra_turn_left",
        "Straight" => "context_ra_forward", "Finish" => "context_ra_finish",
        "RightWide" => "context_ra_take_right", "LeftWide" => "context_ra_take_left",
        "RightSharp" => "context_ra_hard_turn_right", "LeftSharp" => "context_ra_hard_turn_left",
        "UTurnRight" => "context_ra_turn_back_right", "UTurnLeft" => "context_ra_turn_back_left",
        _ => ""
    };

    private static bool[]? Mask(Drawable drawable)
    {
        const int size = 128, normalized = 64;
        using var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!);
        if (bitmap is null) return null;
        using var canvas = new Canvas(bitmap);
        var original = new Rect(drawable.Bounds);
        try { drawable.SetBounds(0, 0, size, size); drawable.Draw(canvas); }
        finally { drawable.Bounds = original; original.Dispose(); }
        var pixels = new int[size * size];
        bitmap.GetPixels(pixels, 0, size, 0, 0, size, size);
        int left = size, top = size, right = -1, bottom = -1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if ((uint)pixels[y * size + x] >> 24 >= 64)
                { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right < left || bottom < top) return null;
        var mask = new bool[normalized * normalized];
        for (int y = 0; y < normalized; y++)
            for (int x = 0; x < normalized; x++)
            {
                int sourceX = left + x * (right - left + 1) / normalized;
                int sourceY = top + y * (bottom - top + 1) / normalized;
                mask[y * normalized + x] = (uint)pixels[sourceY * size + sourceX] >> 24 >= 64;
            }
        return mask;
    }
}
