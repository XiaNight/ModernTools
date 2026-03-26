using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows;

namespace ArmouryProtocol;

public partial class LightingEffectPage : PageBase
{
    public override string PageName => "Armoury Lighting";

    private PeripheralInterface _activeInterface;
    private CancellationTokenSource _sendCts;

    private int _r;
    private int _g;
    private int _b;

    public LightingEffectPage()
    {
        InitializeComponent();

        SendOnceButton.Click += async (_, _) => await SendSingleFrameAsync();
        StartButton.Click += async (_, _) => await StartAsync();
        StopButton.Click += (_, _) => Stop();
        SaveButton.Click += SaveProfile;
        EraseButton.Click += EraseProfile;

        DeviceSelection.Instance.OnActiveDeviceDisconnected += Stop;

        UpdateElapsed(TimeSpan.Zero);
    }

    private void SaveProfile(object sender, RoutedEventArgs e)
    {
        int profileIndex = ReadInt(SaveTextBox.Text, 1, min: 1, max: 0xFF);

        byte[] saveProtocol = [0xC0, 0x85, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        ProtocolService.AppendCmd(_activeInterface, saveProtocol, true, (byte)profileIndex);

        LastSentTextBox.Text = $"Sent save profile command for profile index {profileIndex}";
    }

    private void EraseProfile(object sender, RoutedEventArgs e)
    {
        byte[] eraseProtocol = [0xC0, 0x86];
        ProtocolService.AppendCmd(_activeInterface, eraseProtocol, true);

        LastSentTextBox.Text = "Sent erase profiles command";
    }

    private async Task SendSingleFrameAsync()
    {
        try
        {
            Stop();

            var iface = EnsureInterfaceConnected();
            if (iface == null) return;

            var step = ReadInt(StepTextBox.Text, 64, min: 1, max: 255);
            AdvanceColor(step);

            var packets = BuildLightingPackets((byte)_r, (byte)_g, (byte)_b);
            await SendPacketsAsync(iface, packets, intervalMs: 0, CancellationToken.None);

            SetStatus($"Sent one frame: R={_r:X2} G={_g:X2} B={_b:X2}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private async Task StartAsync()
    {
        var sw = new Stopwatch();
        try
        {
            Stop();

            var iface = EnsureInterfaceConnected();
            if (iface == null) return;

            var frameCount = ReadInt(FrameCountTextBox.Text, 60, min: 1, max: 1000000);
            var intervalMs = ReadDouble(IntervalMsTextBox.Text, 10, min: 0, max: 60000);
            var step = ReadInt(StepTextBox.Text, 64, min: 1, max: 255);

            _sendCts = new CancellationTokenSource();

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            SendOnceButton.IsEnabled = false;

            UpdateElapsed(TimeSpan.Zero);
            SetStatus($"Running... frames={frameCount}, intervalMs={intervalMs:0.###}, step={step}");

            sw.Start();

            await Task.Run(async () =>
            {
                for (int i = 1; i <= frameCount; i++)
                {
                    _sendCts.Token.ThrowIfCancellationRequested();

                    AdvanceColor(step);
                    var packets = BuildLightingPackets((byte)_r, (byte)_g, (byte)_b);

                    await SendPacketsAsync(iface, packets, intervalMs, _sendCts.Token).ConfigureAwait(false);

                    UpdateLastSent(i, frameCount, packets);

                    // Update elapsed occasionally (cheap + avoids spamming dispatcher)
                    if ((i & 0x7) == 0)
                        UpdateElapsed(sw.Elapsed);
                }
            }, _sendCts.Token);

            sw.Stop();
            UpdateElapsed(sw.Elapsed);
            SetStatus($"Done. Elapsed: {FormatElapsed(sw.Elapsed)}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            UpdateElapsed(sw.Elapsed);
            SetStatus($"Stopped. Elapsed: {FormatElapsed(sw.Elapsed)}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            UpdateElapsed(sw.Elapsed);
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            SendOnceButton.IsEnabled = true;
        }
    }

    private void Stop()
    {
        try { _sendCts?.Cancel(); } catch { }
        _sendCts = null;

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SendOnceButton.IsEnabled = true;
    }

    private PeripheralInterface EnsureInterfaceConnected()
    {
        var dev = DeviceSelection.Instance.ActiveDevice;
        if (dev == null)
        {
            SetStatus("No active device. Select a device and click Connect.");
            return null;
        }

        // Reuse existing if still connected
        if (_activeInterface != null && _activeInterface.IsDeviceConnected)
            return _activeInterface;

        _activeInterface = null;

        (ushort usagePage, ushort usageId)? filter = TryReadUsageFilter();

        var detail = dev.interfaces
            .OfType<IPeripheralDetail>()
            .FirstOrDefault(d => filter == null || (d.UsagePage == filter.Value.usagePage && d.Usage == filter.Value.usageId));

        if (detail == null)
        {
            SetStatus("No matching interface found on active device.");
            return null;
        }

        // Connect with async reads disabled (we only write)
        var iface = detail.Connect(false);
        if (iface == null)
        {
            SetStatus("Failed to open interface.");
            return null;
        }

        _activeInterface = iface;
        SetStatus($"Connected: {dev} (usagePage=0x{detail.UsagePage:X4}, usageId=0x{detail.Usage:X4})");
        return _activeInterface;
    }

    private (ushort usagePage, ushort usageId)? TryReadUsageFilter()
    {
        var upText = (UsagePageTextBox.Text ?? string.Empty).Trim();
        var uText = (UsageIdTextBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(upText) || string.IsNullOrWhiteSpace(uText))
            return null;

        if (!TryParseUShortHex(upText, out var usagePage) || !TryParseUShortHex(uText, out var usageId))
            return null;

        return (usagePage, usageId);
    }

    private static bool TryParseUShortHex(string text, out ushort value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static int ReadInt(string text, int fallback, int min, int max)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var v)) v = fallback;
        if (v < min) v = min;
        if (v > max) v = max;
        return v;
    }

    private static double ReadDouble(string text, double fallback, double min, double max)
    {
        // support both "1.1" and "1,1" by trying invariant first, then current culture
        var s = (text ?? string.Empty).Trim();
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
            !double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
        {
            v = fallback;
        }

        if (v < min) v = min;
        if (v > max) v = max;
        return v;
    }

    private void AdvanceColor(int step)
    {
        // Similar to the .bat: R += step; if overflow => reset R and increment G, etc.
        _r += step;
        if (_r >= 256)
        {
            _r = 0;
            _g += step;
            if (_g >= 256)
            {
                _g = 0;
                _b += step;
                if (_b >= 256)
                    _b = 0;
            }
        }
    }

    private static IReadOnlyList<byte[]> BuildLightingPackets(byte r, byte g, byte b)
    {
        // Based on the provided script. Each packet:
        // usb_hid_cmd.exe w PID USAGE_PAGE C0 84 {packetBytes...}
        // We send only the payload after C0 84 (device-specific command prefix goes into the report payload).

        var headers = new (byte b0, byte b1)[]
        {
            (0x72, 0x53),
            (0x5F, 0x13),
            (0x4C, 0x13),
            (0x39, 0x13),
            (0x26, 0x13),
            (0x13, 0x93),
        };

        // Repeated RGB blocks; ends with 00 00 00.
        // Note: keep in sync with your protocol expectation.
        const int rgbTriplets = 19;

        var list = new List<byte[]>(headers.Length);
        foreach (var (b0, b1) in headers)
        {
            var payload = new byte[2 + rgbTriplets * 3];
            payload[0] = b0;
            payload[1] = b1;

            int offset = 2;
            for (int i = 0; i < rgbTriplets; i++)
            {
                payload[offset++] = r;
                payload[offset++] = g;
                payload[offset++] = b;
            }

            list.Add(payload);
        }

        return list;
    }

    private static async Task SendPacketsAsync(PeripheralInterface iface, IReadOnlyList<byte[]> packets, double intervalMs, CancellationToken ct)
    {
        if (iface == null) return;

        foreach (var p in packets)
        {
            ct.ThrowIfCancellationRequested();

            // Prefix according to batch: "C0 84" + payload
            var cmd = new byte[2 + p.Length];
            cmd[0] = 0xC0;
            cmd[1] = 0x84;
            Buffer.BlockCopy(p, 0, cmd, 2, p.Length);

            await iface.Write(cmd).ConfigureAwait(false);

            if (intervalMs > 0)
                await DelayMsAsync(intervalMs, ct).ConfigureAwait(false);
        }
    }

    private static Task DelayMsAsync(double ms, CancellationToken ct)
    {
        if (ms <= 0) return Task.CompletedTask;

        var target = TimeSpan.FromMilliseconds(ms);

        // For larger delays use Task.Delay (yield), but keep a small spin tail for better accuracy.
        if (target >= TimeSpan.FromMilliseconds(2))
        {
            var coarse = target - TimeSpan.FromMilliseconds(1);
            return DelayCoarseThenSpinAsync(coarse, target, ct);
        }

        // For tiny delays just spin.
        return SpinDelayAsync(target, ct);
    }

    private static async Task DelayCoarseThenSpinAsync(TimeSpan coarse, TimeSpan total, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (coarse > TimeSpan.Zero)
            await Task.Delay(coarse, ct).ConfigureAwait(false);

        var remaining = total - sw.Elapsed;
        if (remaining > TimeSpan.Zero)
            await SpinDelayAsync(remaining, ct).ConfigureAwait(false);
    }

    private static Task SpinDelayAsync(TimeSpan time, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < time)
            {
                ct.ThrowIfCancellationRequested();
                Thread.SpinWait(50);
            }
        }, ct);
    }

    private static string FormatElapsed(TimeSpan ts)
        => $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";

    private void UpdateLastSent(int frameIndex, int frameCount, IReadOnlyList<byte[]> packets)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Frame {frameIndex}/{frameCount} - R={_r:X2} G={_g:X2} B={_b:X2}");
            for (int i = 0; i < packets.Count; i++)
            {
                sb.Append($"Pkt{i}: C0-84-");
                sb.AppendLine(BitConverter.ToString(packets[i]));
            }

            Dispatcher.Invoke(() =>
            {
                LastSentTextBox.Text = sb.ToString();
            });
        }
        catch { }
    }

    private void SetStatus(string text)
    {
        try
        {
            Dispatcher.Invoke(() => StatusTextBlock.Text = text);
        }
        catch { }
    }

    private void UpdateElapsed(TimeSpan ts)
    {
        try
        {
            Dispatcher.Invoke(() =>
            {
                var tb = FindName("ElapsedTextBlock") as System.Windows.Controls.TextBlock;
                if (tb != null)
                    tb.Text = FormatElapsed(ts);
            });
        }
        catch { }
    }
}
