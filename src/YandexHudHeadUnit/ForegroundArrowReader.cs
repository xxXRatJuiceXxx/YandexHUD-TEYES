using Android.AccessibilityServices;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Java.Util.Concurrent;

namespace YandexNotificationBridge;

// Used only after the user opts in. Pixels are never saved or transmitted.
// A window-specific capture is preferred on Android 14; Android 11–13 uses
// the display API only while Navigator is the focused active window.
internal sealed class ForegroundArrowReader
{
    private int _pending;
    private static string _status = "Выключено";
    internal static string Status => Volatile.Read(ref _status);
    internal static void Report(string value) => Volatile.Write(ref _status, value);

    internal async Task<string?> ReadAsync(AccessibilityService service, Handler worker, int windowId, ScreenArea area)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) { Report("Нужен Android 11 или новее"); return null; }
        if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return null;
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        long started = SystemClock.UptimeMillis();
        try
        {
            using var root = service.RootInActiveWindow;
            using var window = root?.Window;
            if (root is null || !NavigationSource.AcceptsPackage(root.PackageName?.ToString()) ||
                root.WindowId != windowId || window is null || !window.IsActive || !window.IsFocused ||
                ((Android.App.KeyguardManager?)service.GetSystemService(Context.KeyguardService))?.IsKeyguardLocked == true)
            { Interlocked.Exchange(ref _pending, 0); return null; }

            ScreenArea frame;
            bool windowCapture = OperatingSystem.IsAndroidVersionAtLeast(34);
            if (windowCapture)
            {
                using var bounds = new Rect(); window.GetBoundsInScreen(bounds);
                frame = new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            }
            else
            {
                using var manager = (IWindowManager?)service.GetSystemService(Context.WindowService);
                using var metrics = new Android.Util.DisplayMetrics();
                manager?.DefaultDisplay?.GetRealMetrics(metrics);
                frame = new(0, 0, metrics.WidthPixels, metrics.HeightPixels);
            }
            var callback = new CaptureCallback(this, service, result, area, frame, windowId, started);
            var executor = new WorkerExecutor(worker);
            // The callback owns both Java objects until the platform finishes,
            // including callbacks arriving after our timeout.
            callback.Executor = executor;
            if (windowCapture) service.TakeScreenshotOfWindow(windowId, executor, callback);
            else service.TakeScreenshot(Display.DefaultDisplay, executor, callback);
            return await result.Task.WaitAsync(TimeSpan.FromMilliseconds(900));
        }
        catch (System.TimeoutException) { Report("Ожидание кадра Android"); return null; }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _pending, 0);
            Report("Чтение значка недоступно: " + ex.GetType().Name);
            return null;
        }
    }

    private sealed class WorkerExecutor(Handler handler) : Java.Lang.Object, IExecutor
    {
        public void Execute(Java.Lang.IRunnable? command) { if (command is not null) handler.Post(command); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
    private sealed class CaptureCallback(ForegroundArrowReader owner, AccessibilityService service,
        TaskCompletionSource<string?> completion, ScreenArea area, ScreenArea frame, int windowId, long started)
        : Java.Lang.Object, AccessibilityService.ITakeScreenshotCallback
    {
        internal WorkerExecutor? Executor;
        public void OnFailure(int errorCode)
        {
            Report(errorCode == (int)Android.Accessibilityservice.AccessibilityService.TakeScreenshotError.NoAccessibilityAccess
                ? "Выключите и снова включите службу чтения навигации в специальных возможностях"
                : "Android не предоставил значок: " + errorCode);
            Finish(null);
        }
        public void OnSuccess(AccessibilityService.ScreenshotResult screenshot)
        {
            string? direction = null;
            try
            {
                using (screenshot)
                using (var buffer = screenshot.HardwareBuffer)
                {
                    try
                    {
                        using var currentRoot = service.RootInActiveWindow;
                        if (!YandexHudHeadUnit.Options.VisualArrows || !YandexScreenService.Connected ||
                            currentRoot is null || currentRoot.WindowId != windowId ||
                            !NavigationSource.AcceptsPackage(currentRoot.PackageName?.ToString()) ||
                            screenshot.Timestamp < started || SystemClock.UptimeMillis() - started > 1200) return;
                        using var hardware = Bitmap.WrapHardwareBuffer(buffer, screenshot.ColorSpace);
                        if (hardware is null || hardware.Width != frame.Width || hardware.Height != frame.Height ||
                            area.Left < frame.Left || area.Top < frame.Top || area.Right > frame.Right || area.Bottom > frame.Bottom)
                        { Report("Изменилась геометрия окна; ожидаю следующий кадр"); return; }
                        // Crop before creating a CPU-readable bitmap. Only the
                        // identified current-arrow view reaches the recognizer.
                        using var region = Bitmap.CreateBitmap(hardware, area.Left - frame.Left, area.Top - frame.Top, area.Width, area.Height);
                        using var crop = region?.Copy(Bitmap.Config.Argb8888!, false);
                        if (crop is null) return;
                        var pixels = new int[crop.Width * crop.Height];
                        crop.GetPixels(pixels, 0, crop.Width, 0, 0, crop.Width, crop.Height);
                        if (ScreenArrowMask.Extract(pixels, crop.Width, crop.Height) is { } mask)
                        {
                            using var navigator = service.CreatePackageContext(NavigationSource.PackageName, PackageContextFlags.IgnoreSecurity)!;
                            direction = ManeuverRecognizer.RecognizeMask(navigator, mask);
                        }
                        Array.Clear(pixels);
                        Report($"Значок экрана: {direction ?? "не распознан"}; {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
                    }
                    finally { buffer.Close(); }
                }
            }
            catch (Exception ex) { Report("Не удалось прочитать значок: " + ex.GetType().Name); }
            finally { Finish(direction); }
        }
        private void Finish(string? direction)
        {
            completion.TrySetResult(direction);
            Interlocked.Exchange(ref owner._pending, 0);
            Executor?.Dispose(); Executor = null;
            Dispose();
        }
    }
}
