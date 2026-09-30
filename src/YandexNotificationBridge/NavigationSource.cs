namespace YandexNotificationBridge;

// Each APK has one explicit source. The existing phone bridge stays on Maps;
// the standalone head-unit APK uses Navigator. Never combine their routes.
internal static class NavigationSource
{
#if HEADUNIT
    internal const string PackageName = "ru.yandex.yandexnavi";
    internal const string DisplayName = "Яндекс Навигатор";
#else
    internal const string PackageName = "ru.yandex.yandexmaps";
    internal const string DisplayName = "Яндекс Карты";
#endif
    internal const string ResourcePrefix = PackageName + ":id/";
    internal static bool AcceptsPackage(string? package) => string.Equals(package, PackageName, StringComparison.Ordinal);
}
