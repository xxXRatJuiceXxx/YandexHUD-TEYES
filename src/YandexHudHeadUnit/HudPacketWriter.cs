namespace YandexHudHeadUnit;

internal static class HudPacketWriter
{
    // Observe graceful-stop cancellation between complete ^...$ frames. Cutting
    // one in half can make the peripheral discard the next reset command too.
    // Each native write supplied by BleHud has its own bounded timeout.
    internal static async Task SendAsync(byte[][] frames, Func<byte[], Task> write,
        CancellationToken token, Func<Task>? pace = null)
    {
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            for (int offset = 0; offset < frame.Length; offset += 20)
            {
                await write(frame[offset..Math.Min(offset + 20, frame.Length)]);
                if (pace is null) await Task.Delay(50);
                else await pace();
            }
        }
        token.ThrowIfCancellationRequested();
    }
}
