namespace YandexNotificationBridge;

// An opaque screen crop has no useful alpha channel. Estimate its solid
// background at the border, then compare only contrasting foreground pixels.
internal static class ScreenArrowMask
{
    internal static bool[]? Extract(int[] pixels, int width, int height)
    {
        if (width < 16 || height < 16 || width > 640 || height > 640 || pixels.Length != width * height) return null;
        var border = new List<int>();
        for (int x = 0; x < width; x++) { border.Add(pixels[x]); border.Add(pixels[(height - 1) * width + x]); }
        for (int y = 1; y < height - 1; y++) { border.Add(pixels[y * width]); border.Add(pixels[y * width + width - 1]); }
        int Median(int shift) => border.Select(c => (c >> shift) & 255).Order().ElementAt(border.Count / 2);
        int r = Median(16), g = Median(8), b = Median(0);
        int Difference(int c) => Math.Max(Math.Abs(((c >> 16) & 255) - r), Math.Max(Math.Abs(((c >> 8) & 255) - g), Math.Abs((c & 255) - b)));
        // A map, text, overlay, clipped icon, or gradient is not a solid balloon.
        if (border.Count(c => Difference(c) > 35) > border.Count / 20) return null;
        var occupied = new bool[pixels.Length];
        int left = width, top = height, right = -1, bottom = -1, count = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (Difference(pixels[y * width + x]) >= 90)
                {
                    occupied[y * width + x] = true; count++;
                    left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        if (count < pixels.Length * .025 || count > pixels.Length * .65 || left <= 0 || top <= 0 || right >= width - 1 || bottom >= height - 1) return null;
        var mask = new bool[64 * 64];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                mask[y * 64 + x] = occupied[(top + y * (bottom - top + 1) / 64) * width + left + x * (right - left + 1) / 64];
        return mask;
    }
}
