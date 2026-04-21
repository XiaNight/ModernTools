using Base.Pages;
using Base.Services;
using Base.Services.APIService;
using Base.Services.Peripheral;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;

// Alias to avoid ambiguity between Base.Services.Debug and System.Diagnostics.Debug
using Debug = Base.Services.Debug;

namespace ArmouryProtocol;

// ---------------------------------------------------------------------------
// Parameter bag – can be set externally before or between runs
// ---------------------------------------------------------------------------
public sealed class StressTestParameters
{
    /// <summary>Hex command bytes (e.g. "C0 01 00 00"). Parsed on Start.</summary>
    public string CommandHex { get; set; } = "12 00 00 00";

    /// <summary>Delay between consecutive sends, in milliseconds (0 = no delay).</summary>
    public int IntervalMs { get; set; } = 10;

    /// <summary>
    /// How long to run in seconds. 0 = no time limit.
    /// The test stops when whichever limit (duration or count) is hit first.
    /// Both 0 = run until Stop() is called.
    /// </summary>
    public int DurationSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum number of sends before stopping. 0 = no count limit.
    /// The test stops when whichever limit (duration or count) is hit first.
    /// Both 0 = run until Stop() is called.
    /// </summary>
    public long MaxSendCount { get; set; } = 0;

    /// <summary>When true each send uses WriteAndReadAsync; timeouts count as failures.</summary>
    public bool WaitForResponse { get; set; } = false;

    /// <summary>Per-send response timeout in milliseconds (only used when WaitForResponse = true).</summary>
    public int ResponseTimeoutMs { get; set; } = 100;

    /// <summary>
    /// How many times to retry a timed-out send before counting it as a failure.
    /// 0 = no retries (first timeout = fail). Only applies when WaitForResponse = true.
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>HID Usage Page filter (hex string, e.g. "FF00"). Empty = use first interface.</summary>
    public string UsagePage { get; set; } = "FF00";

    /// <summary>HID Usage Id filter (hex string, e.g. "0001"). Empty = use first interface.</summary>
    public string UsageId { get; set; } = "0001";
}

// ---------------------------------------------------------------------------
// Live / final result – updated throughout the run, readable at any time
// ---------------------------------------------------------------------------
public sealed class StressTestResult
{
    public bool IsRunning { get; internal set; }
    public long SendCount { get; internal set; }
    public long FailCount { get; internal set; }
    public TimeSpan Elapsed { get; internal set; }

    /// <summary>Null while running; set when the run finishes or is stopped.</summary>
    public string FinalStatus { get; internal set; }

    /// <summary>True if the last completed run ended without being stopped or errored.</summary>
    public bool CompletedSuccessfully { get; internal set; }

    public long SuccessCount => SendCount - FailCount;

    internal void Reset()
    {
        IsRunning = false;
        SendCount = 0;
        FailCount = 0;
        Elapsed = TimeSpan.Zero;
        FinalStatus = null;
        CompletedSuccessfully = false;
    }
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------
public partial class ProtocolStressTest : PageBase
{
    public override string PageName => "Protocol Stress Test";

    // ── public state ────────────────────────────────────────────────────────
    public StressTestParameters Parameters { get; } = new();
    public bool IsRunning => _result.IsRunning;

    // ── private state ───────────────────────────────────────────────────────
    private PeripheralInterface _activeInterface;
    private CancellationTokenSource _sendCts;
    private readonly StressTestResult _result = new();

    // ── ctor ────────────────────────────────────────────────────────────────
    public ProtocolStressTest()
    {
        InitializeComponent();

        StartButton.Click += async (_, _) => await StartAsync();
        StopButton.Click += (_, _) => Stop();

        DeviceSelection.Instance.OnActiveDeviceDisconnected += Stop;

        // Keep Parameters in sync when the user edits text boxes directly
        CommandTextBox.TextChanged           += (_, _) => Parameters.CommandHex         = CommandTextBox.Text;
        IntervalMsTextBox.TextChanged        += (_, _) => Parameters.IntervalMs         = ReadInt(IntervalMsTextBox.Text,         Parameters.IntervalMs,         0,   60000);
        DurationSecondsTextBox.TextChanged   += (_, _) => Parameters.DurationSeconds    = ReadInt(DurationSecondsTextBox.Text,    Parameters.DurationSeconds,    0,   int.MaxValue);
        MaxSendCountTextBox.TextChanged      += (_, _) => Parameters.MaxSendCount       = ReadLong(MaxSendCountTextBox.Text,      Parameters.MaxSendCount,       0,   long.MaxValue);
        ResponseTimeoutMsTextBox.TextChanged += (_, _) => Parameters.ResponseTimeoutMs  = ReadInt(ResponseTimeoutMsTextBox.Text,  Parameters.ResponseTimeoutMs,  1,   60000);
        RetryCountTextBox.TextChanged        += (_, _) => Parameters.RetryCount         = ReadInt(RetryCountTextBox.Text,         Parameters.RetryCount,         0,   100);
        UsagePageTextBox.TextChanged         += (_, _) => Parameters.UsagePage          = UsagePageTextBox.Text;
        UsageIdTextBox.TextChanged           += (_, _) => Parameters.UsageId            = UsageIdTextBox.Text;

        UpdateElapsed(TimeSpan.Zero);
        UpdateSendCount(0);
        UpdateFailCount(0);
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>Returns a snapshot of the current (or most-recent) run's result.</summary>
    [GET]
    public StressTestResult GetResult() => new()
    {
        IsRunning            = _result.IsRunning,
        SendCount            = _result.SendCount,
        FailCount            = _result.FailCount,
        Elapsed              = _result.Elapsed,
        FinalStatus          = _result.FinalStatus,
        CompletedSuccessfully = _result.CompletedSuccessfully,
    };

    // ── Parameter setters (sync backing field + UI) ─────────────────────────
    [POST]
    public void SetCommand(string commandHex)
    {
        Parameters.CommandHex = commandHex ?? string.Empty;
        Dispatcher.Invoke(() => CommandTextBox.Text = Parameters.CommandHex);
    }
    [POST]
    public void SetIntervalMs(int ms)
    {
        Parameters.IntervalMs = Math.Max(0, ms);
        Dispatcher.Invoke(() => IntervalMsTextBox.Text = Parameters.IntervalMs.ToString());
    }
    [POST]
    public void SetDurationSeconds(int seconds)
    {
        Parameters.DurationSeconds = Math.Max(0, seconds);
        Dispatcher.Invoke(() => DurationSecondsTextBox.Text = Parameters.DurationSeconds.ToString());
    }
    [POST]
    public void SetMaxSendCount(long count)
    {
        Parameters.MaxSendCount = Math.Max(0, count);
        Dispatcher.Invoke(() => MaxSendCountTextBox.Text = Parameters.MaxSendCount.ToString());
    }
    [POST]
    public void SetWaitForResponse(bool wait)
    {
        Parameters.WaitForResponse = wait;
        Dispatcher.Invoke(() =>
        {
            WaitForResponseCheckBox.IsChecked  = wait;
            ResponseTimeoutMsTextBox.IsEnabled = wait;
            RetryCountTextBox.IsEnabled        = wait;
        });
    }
    [POST]
    public void SetResponseTimeoutMs(int ms)
    {
        Parameters.ResponseTimeoutMs = Math.Max(1, ms);
        Dispatcher.Invoke(() => ResponseTimeoutMsTextBox.Text = Parameters.ResponseTimeoutMs.ToString());
    }
    [POST]
    public void SetRetryCount(int retries)
    {
        Parameters.RetryCount = Math.Max(0, retries);
        Dispatcher.Invoke(() => RetryCountTextBox.Text = Parameters.RetryCount.ToString());
    }
    [POST]
    public void SetUsageFilter(string usagePage, string usageId)
    {
        Parameters.UsagePage = usagePage ?? string.Empty;
        Parameters.UsageId   = usageId   ?? string.Empty;
        Dispatcher.Invoke(() =>
        {
            UsagePageTextBox.Text = Parameters.UsagePage;
            UsageIdTextBox.Text   = Parameters.UsageId;
        });
    }

    // ── Start / Stop ─────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the stress test. Does nothing and returns false if already running.
    /// Reads parameters from <see cref="Parameters"/>; any previous
    /// programmatic calls to Set* are reflected automatically.
    /// </summary>
    [POST(requireMainThread:true)]
    public async Task<bool> StartAsync()
    {
        if (IsRunning) return false;

        await RunTestAsync();
        return true;
    }

    /// <summary>
    /// Stops the currently running stress test. Safe to call when idle.
    /// </summary>
    [POST(requireMainThread:true)]
    public void Stop()
    {
        try { _sendCts?.Cancel(); } catch { }
        _sendCts = null;

        Dispatcher.Invoke(() =>
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled  = false;
        });
    }

    // =========================================================================
    // XAML event handlers (thin pass-throughs)
    // =========================================================================

    private void WaitForResponseCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool wait = WaitForResponseCheckBox.IsChecked == true;
        ResponseTimeoutMsTextBox.IsEnabled = wait;
        RetryCountTextBox.IsEnabled        = wait;
        Parameters.WaitForResponse = wait;
    }

    // =========================================================================
    // CORE TEST LOOP
    // =========================================================================

    private async Task RunTestAsync()
    {
        var cmd             = ParseHexCommand(Parameters.CommandHex);
        int intervalMs      = Parameters.IntervalMs;
        int durationSec     = Parameters.DurationSeconds;   // 0 = no time limit
        long maxSendCount   = Parameters.MaxSendCount;      // 0 = no count limit
        bool waitForResp    = Parameters.WaitForResponse;
        int responseTimeout = Parameters.ResponseTimeoutMs;
        int retryCount      = Parameters.RetryCount;

        if (cmd == null || cmd.Length == 0)
        {
            SetStatus("Invalid command. Enter hex bytes separated by spaces (e.g. C0 01 00 00).");
            return;
        }

        var iface = EnsureInterfaceConnected();
        if (iface == null) return;

        _result.Reset();
        _result.IsRunning = true;

        _sendCts = new CancellationTokenSource();
        if (durationSec > 0)
            _sendCts.CancelAfter(TimeSpan.FromSeconds(durationSec));

        StartButton.IsEnabled = false;
        StopButton.IsEnabled  = true;

        UpdateElapsed(TimeSpan.Zero);
        UpdateSendCount(0);
        UpdateFailCount(0);

        string limitText = (durationSec > 0, maxSendCount > 0) switch
        {
            (true,  true)  => $"duration={durationSec}s or {maxSendCount} sends",
            (true,  false) => $"duration={durationSec}s",
            (false, true)  => $"max {maxSendCount} sends",
            _              => "indefinite",
        };
        SetStatus($"Running... interval={intervalMs} ms, {limitText}" +
                  (waitForResp ? $", wait for response (timeout={responseTimeout}ms, retries={retryCount})" : string.Empty));

        var sw = Stopwatch.StartNew();
        var ct = _sendCts.Token;

        try
        {
            await Task.Run(async () =>
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // Count limit reached — cancel cleanly via the token
                    if (maxSendCount > 0 && _result.SendCount >= maxSendCount)
                    {
                        _sendCts.Cancel();
                        ct.ThrowIfCancellationRequested();
                    }

                    if (waitForResp)
                    {
                        bool success = false;
                        for (int attempt = 0; attempt <= retryCount; attempt++)
                        {
                            ct.ThrowIfCancellationRequested();

                            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            timeoutCts.CancelAfter(responseTimeout);
                            try
                            {
                                await iface.WriteAndReadAsync(cmd, timeoutCts.Token).ConfigureAwait(false);
                                success = true;
                                break;
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                // Per-attempt timeout — retry if attempts remain
                            }
                        }

                        _result.SendCount++;
                        if (!success) _result.FailCount++;
                    }
                    else
                    {
                        await iface.Write(cmd).ConfigureAwait(false);
                        _result.SendCount++;
                    }

                    _result.Elapsed = sw.Elapsed;

                    if ((_result.SendCount & 0xF) == 0)
                    {
                        long s = _result.SendCount;
                        long f = _result.FailCount;
                        TimeSpan elapsed = sw.Elapsed;
                        Dispatcher.Invoke(() =>
                        {
                            UpdateElapsed(elapsed);
                            UpdateSendCount(s);
                            UpdateFailCount(f);
                        });
                    }

                    if (intervalMs > 0)
                        await Task.Delay(intervalMs, ct).ConfigureAwait(false);
                }
            }, ct);

            // Unreachable with while(true), kept for future-proofing
            sw.Stop();
            _result.Elapsed = sw.Elapsed;
            _result.CompletedSuccessfully = true;
            _result.FinalStatus = BuildFinalStatus("Done");
            SetStatus(_result.FinalStatus);
            Debug.Log($"[ProtocolStressTest] {_result.FinalStatus}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _result.Elapsed = sw.Elapsed;
            // Completed successfully if stopped by a limit (duration or count), not by manual Stop()
            bool stoppedByLimit = (durationSec > 0 || maxSendCount > 0) && _sendCts == null;
            _result.CompletedSuccessfully = stoppedByLimit;
            _result.FinalStatus = BuildFinalStatus(stoppedByLimit ? "Done" : "Stopped");
            SetStatus(_result.FinalStatus);
            Debug.Log($"[ProtocolStressTest] {_result.FinalStatus}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _result.Elapsed = sw.Elapsed;
            _result.CompletedSuccessfully = false;
            _result.FinalStatus = $"Error: {ex.Message}";
            SetStatus(_result.FinalStatus);
            Debug.Log($"[ProtocolStressTest] {_result.FinalStatus}");
        }
        finally
        {
            _result.IsRunning = false;
            UpdateElapsed(_result.Elapsed);
            UpdateSendCount(_result.SendCount);
            UpdateFailCount(_result.FailCount);
            StartButton.IsEnabled = true;
            StopButton.IsEnabled  = false;
        }
    }

    private string BuildFinalStatus(string prefix)
        => $"{prefix}. Elapsed: {FormatElapsed(_result.Elapsed)}, sends: {_result.SendCount}, fails: {_result.FailCount}";

    // =========================================================================
    // HELPERS
    // ========================================================================

    private PeripheralInterface EnsureInterfaceConnected()
    {
        var dev = DeviceSelection.Instance.ActiveDevice;
        if (dev == null)
        {
            SetStatus("No active device. Select a device and click Connect.");
            return null;
        }

        bool hasFilter = TryReadUsageFilter(out ushort filterPage, out ushort filterUsage);

        // Reuse only if still connected AND the interface matches the current filter
        if (_activeInterface != null && _activeInterface.IsDeviceConnected)
        {
            var current = _activeInterface.ProductInfo;
            bool filterMatches = !hasFilter
                || (current.UsagePage == filterPage && current.Usage == filterUsage);

            if (filterMatches)
                return _activeInterface;
        }

        _activeInterface = null;

        var detail = dev.interfaces
            .OfType<IPeripheralDetail>()
            .FirstOrDefault(d => !hasFilter
                              || (d.UsagePage == filterPage && d.Usage == filterUsage));

        if (detail == null)
        {
            SetStatus("No matching interface found on active device.");
            return null;
        }

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

    private bool TryReadUsageFilter(out ushort usagePage, out ushort usageId)
    {
        usagePage = 0;
        usageId   = 0;

        var upText = (UsagePageTextBox.Text ?? string.Empty).Trim();
        var uText  = (UsageIdTextBox.Text   ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(upText) || string.IsNullOrWhiteSpace(uText))
            return false;

        if (!TryParseUShortHex(upText, out usagePage) || !TryParseUShortHex(uText, out usageId))
            return false;

        return true;
    }

    private static bool TryParseUShortHex(string text, out ushort value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static byte[] ParseHexCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var parts  = text.Trim().Split([' ', '-', ','], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<byte>(parts.Length);

        foreach (var part in parts)
        {
            var s = part.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            if (!byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                return null;
            result.Add(b);
        }

        return result.Count > 0 ? result.ToArray() : null;
    }

    private static int ReadInt(string text, int fallback, int min, int max)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var v)) v = fallback;
        return Math.Clamp(v, min, max);
    }

    private static long ReadLong(string text, long fallback, long min, long max)
    {
        if (!long.TryParse((text ?? string.Empty).Trim(), out var v)) v = fallback;
        return Math.Clamp(v, min, max);
    }

    private static string FormatElapsed(TimeSpan ts)
        => $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";

    private void SetStatus(string text)
    {
        try { Dispatcher.Invoke(() => StatusTextBlock.Text = text); } catch { }
    }

    private void UpdateElapsed(TimeSpan ts)
    {
        try { Dispatcher.Invoke(() => ElapsedTextBlock.Text = FormatElapsed(ts)); } catch { }
    }

    private void UpdateSendCount(long count)
    {
        try { Dispatcher.Invoke(() => SendCountTextBlock.Text = count.ToString()); } catch { }
    }

    private void UpdateFailCount(long count)
    {
        try { Dispatcher.Invoke(() => FailCountTextBlock.Text = count.ToString()); } catch { }
    }
}
