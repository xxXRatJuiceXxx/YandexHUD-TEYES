using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;

namespace YandexNotificationBridge;

// Reads the bitmaps supplied by Maps in its RemoteViews. No screen capture.
// Only simple, unambiguous lanes are accepted. Compound arrows and bus lanes
// suppress the whole row so that lane positions cannot shift on the HUD.
internal static class LaneRecognizer
{
    private static readonly (string Name, string Code)[] Directions =
    [ ("straightahead", "S"), ("left90", "L"), ("right90", "R"), ("left180", "U"), ("right45", "SR") ];
    private static readonly Dictionary<string, bool[]> Templates = new();

    internal static string? Recognize(Context maps, Drawable drawable)
    {
        var (all, bright) = Masks(drawable);
        if (all is null) return null;
        bool selected = bright is not null;
        var live = bright ?? all;
        var scores = new Dictionary<string, double>();
        foreach (var direction in Directions)
        {
            double score = 0;
            foreach (string suffix in new[] { "large", "small", "large_24", "small_24" })
            {
                string name = "context_lane_" + direction.Name + "_" + suffix;
                if (!Templates.TryGetValue(name, out var template))
                {
                    int id = maps.Resources?.GetIdentifier(name, "drawable", NavigationSource.PackageName) ?? 0;
                    if (id == 0) continue;
                    using var reference = maps.GetDrawable(id);
                    if (reference is null || Masks(reference).All is not { } mask) continue;
                    Templates[name] = template = mask;
                }
                int intersection = 0, union = 0;
                for (int i = 0; i < live.Length; i++)
                {
                    if (live[i] && template[i]) intersection++;
                    if (live[i] || template[i]) union++;
                }
                score = Math.Max(score, union == 0 ? 0 : (double)intersection / union);
            }
            scores[direction.Code] = score;
        }
        var ranked = scores.OrderByDescending(p => p.Value).ToArray();
        if (ranked[0].Value < .85 || ranked[0].Value - ranked[1].Value < .12) return null;
        return ranked[0].Key + (selected ? "!" : "");
    }

    private static (bool[]? All, bool[]? Bright) Masks(Drawable drawable)
    {
        const int size = 128;
        using var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(bitmap);
        using var original = new Rect(drawable.Bounds);
        try { drawable.SetBounds(0, 0, size, size); drawable.Draw(canvas); }
        finally { drawable.Bounds = original; }
        var pixels = new int[size * size];
        bitmap.GetPixels(pixels, 0, size, 0, 0, size, size);
        bool Present(int p, bool bright)
        {
            int alpha = (int)((uint)p >> 24);
            int light = Math.Max((p >> 16) & 255, Math.Max((p >> 8) & 255, p & 255));
            return bright ? alpha * light / 255 >= 180 : alpha >= 48;
        }
        bool[]? Normalize(bool bright)
        {
            int left = size, top = size, right = -1, bottom = -1, count = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (Present(pixels[y * size + x], bright))
                    { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); count++; }
            if (count < 24 || right <= left || bottom <= top) return null;
            const int normalized = 64;
            var mask = new bool[normalized * normalized];
            for (int y = 0; y < normalized; y++)
                for (int x = 0; x < normalized; x++)
                    mask[y * normalized + x] = Present(pixels[(top + y * (bottom - top + 1) / normalized) * size + left + x * (right - left + 1) / normalized], bright);
            return mask;
        }
        return (Normalize(false), Normalize(true));
    }
}
