using System.Globalization;
using System.Text.RegularExpressions;
using WiiYiiHudNavigator.Common.Models;

namespace WiiYiiHudNavigator.Plugin.Yandex;

// A platform-independent parser. The Android adapter must pass only fields from
// one notification, in their original order, and clear the HUD when it is removed.
public sealed record YandexNotification(
    string PackageName,
    string? Title,
    string? Text,
    string? BigText = null);

public enum YandexUpdateKind { Ignore, Unsupported, Navigation, Rerouting, Finished }

public sealed record YandexUpdate(YandexUpdateKind Kind, HudNavigationData? Data = null);

public static class YandexNotificationParser
{
    public const string NavigatorPackage = "ru.yandex.yandexnavi";
    public const string MapsPackage = "ru.yandex.yandexmaps";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Distance = new(
        @"(?<![\p{L}\d.,-])(?<value>\d{1,3}(?: \d{3})+|\d+)(?<fraction>[.,]\d+)?\s*(?<unit>километр\p{L}*|км|km|метр\p{L}*|м|m)(?!\p{L}|/)",
        Options, RegexTimeout);
    private static readonly Regex Then = new(@"\b(?:а\s+)?затем\b|\bпотом\b|\bthen\b", Options, RegexTimeout);

    public static YandexUpdate Parse(YandexNotification notification)
    {
        if (notification.PackageName != NavigatorPackage && notification.PackageName != MapsPackage)
            return new(YandexUpdateKind.Ignore);

        var title = Normalize(notification.Title);
        var text = Normalize(notification.Text);
        var bigText = Normalize(notification.BigText);
        var fields = new[] { title, text, bigText }.Where(x => x.Length > 0).Distinct().ToArray();
        if (fields.Length == 0)
            return new(YandexUpdateKind.Unsupported);

        // Status messages take precedence over any obsolete maneuver in BigText.
        if (Matches(title + "\n" + text,
            @"\b(?:перестроение маршрута|перестраиваем маршрут|поиск маршрута|маршрут перестраивается|recalculating|rerouting)\b"))
            return new(YandexUpdateKind.Rerouting, new()
            {
                IsRerouting = true,
                NextTurnDirection = DirectionInstruction.Rerouting,
                CameraDistance = 0,
                LaneGuidance = new LaneGuidance()
            });

        if (Matches(title + "\n" + text,
            @"\b(?:вы приехали|вы прибыли|маршрут завершен|навигация завершена|you have arrived|you've arrived|navigation ended)\b"))
            return new(YandexUpdateKind.Finished);

        // Do not infer a turn from road names, total route distance, ads or camera alerts.
        foreach (var field in fields)
        {
            var firstInstruction = Then.Split(field, 2)[0];
            var direction = ParseDirection(firstInstruction);
            if (direction is null)
                continue;

            double? meters = ReadDistance(firstInstruction);
            // Yandex may split distance and maneuver between title and body.
            // A distance-only title is accepted; an ETA/route summary is not.
            if (meters is null && field != title && IsDistanceTitle(title))
                meters = ReadDistance(title);
            if (meters is null && Matches(firstInstruction, @"\b(?:сейчас|now)\b"))
                meters = 0;
            if (meters is null)
                return new(YandexUpdateKind.Unsupported);

            return new(YandexUpdateKind.Navigation, new()
            {
                NextTurnDirection = direction,
                NextTurnDistance = meters.Value,
                NextTurnRoadName = firstInstruction.Trim(),
                // No source for ETA, lanes, camera range or second-turn geometry
                // has been verified. In particular, an exit number is not an angle.
                SecondTurnDirection = null,
                CameraDistance = 0,
                LaneGuidance = new LaneGuidance()
            });
        }
        return new(YandexUpdateKind.Unsupported);
    }

    public static double? ReadDistance(string value)
    {
        var match = Distance.Match(Normalize(value));
        if (!match.Success)
            return null;
        var number = (match.Groups["value"].Value + match.Groups["fraction"].Value)
            .Replace(" ", "").Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var distance))
            return null;
        var unit = match.Groups["unit"].Value;
        if (unit.StartsWith("к", StringComparison.OrdinalIgnoreCase) || unit.StartsWith("k", StringComparison.OrdinalIgnoreCase))
            distance *= 1000;
        return double.IsFinite(distance) && distance <= 2_000_000 ? distance : null;
    }

    private static bool IsDistanceTitle(string title) => Matches(title,
        @"^(?:через\s+|in\s+)?[\d\s.,]+\s*(?:км|м|km|m|метр\p{L}*|километр\p{L}*)\s*$");

    private static DirectionInstruction? ParseDirection(string instruction)
    {
        // Roundabout exit numbers do not tell us which of the HUD's angle-specific
        // arrows to show. Wait for verified icon/geometry data instead of guessing.
        if (Matches(instruction, @"\b(?:кругов\p{L}*|кольц\p{L}*|roundabout)\b"))
            return null;
        if (Matches(instruction, @"\b(?:развернитесь|разворот|сделайте разворот|make a u[ -]?turn|u[ -]?turn)\b"))
            return Matches(instruction, @"\b(?:направо|right)\b")
                ? DirectionInstruction.UTurnRight : DirectionInstruction.UTurnLeft;

        if (Matches(instruction, @"\b(?:держитесь|придерживайтесь|keep|bear)\s+(?:правее|справа|right)\b"))
            return DirectionInstruction.RightFork;
        if (Matches(instruction, @"\b(?:держитесь|придерживайтесь|keep|bear)\s+(?:левее|слева|left)\b"))
            return DirectionInstruction.LeftFork;

        if (Matches(instruction, @"\b(?:поверните|поворот|сверните)\s+(?:резко\s+)?направо\b|\bturn\s+(?:sharp\s+|sharply\s+)?right\b"))
            return Matches(instruction, @"\b(?:резко|sharp|sharply)\b")
                ? DirectionInstruction.RightSharpSE : DirectionInstruction.Right;
        if (Matches(instruction, @"\b(?:поверните|поворот|сверните)\s+(?:резко\s+)?налево\b|\bturn\s+(?:sharp\s+|sharply\s+)?left\b"))
            return Matches(instruction, @"\b(?:резко|sharp|sharply)\b")
                ? DirectionInstruction.LeftSharpSE : DirectionInstruction.Left;

        if (Matches(instruction, @"\b(?:двигайтесь|продолжайте движение|езжайте|проедьте)\s+прямо\b|\b(?:go|continue|head)\s+straight\b"))
            return DirectionInstruction.Straight;
        return null;
    }

    private static bool Matches(string value, string pattern) => Regex.IsMatch(value, pattern, Options, RegexTimeout);

    private static string Normalize(string? value) => (value ?? "")[..Math.Min(value?.Length ?? 0, 4096)]
        .Replace('\u00a0', ' ').Replace('\u202f', ' ').Replace('ё', 'е').Replace('Ё', 'Е').Trim();
}
