using System.Globalization;
using System.Text.RegularExpressions;

namespace YandexNotificationBridge;

internal static class CameraText
{
    private static readonly Regex Distance = new(@"^\s*(?<n>(?:\d{1,3}(?: \d{3})+|\d+)(?:[.,]\d+)?)\s*(?<u>м|км|m|km)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static bool IsCameraDescription(string? description, string? localizedLabel) =>
        !string.IsNullOrWhiteSpace(localizedLabel) &&
        string.Equals(description?.Trim(), localizedLabel.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static double ApproachingDistance(string? text)
    {
        // Zero is an explicit HUD cancel. Rear-facing cameras can have negative
        // distances in Maps: those are NOT an approaching camera ahead of the car.
        var match = Distance.Match((text ?? "").Replace('\u00a0', ' ').Replace('\u202f', ' '));
        if (!match.Success || !double.TryParse(match.Groups["n"].Value.Replace(" ", "").Replace(',', '.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var meters)) return 0;
        if (match.Groups["u"].Value.Length == 2) meters *= 1000;
        return double.IsFinite(meters) && meters >= 1 && meters <= 5000 ? meters : 0;
    }
}
