using Base.Pages;
using Base.Services;
using Base.Services.APIService;
using Base.Services.Peripheral;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CommonProtocol;

/// <summary>
/// A page that controls a USB HID relay board with 6 relay channels.
/// Group A (Key 4): relay indices 0–3.
/// Group B (Key 3): relay indices 0–1.
/// Command format: [0x02, key, index, 0x00, state]  state=0x01 ON, 0x00 OFF.
/// </summary>
public partial class RelayControlPage : PageBase
{
    public override string PageName => "Relay Control";
    public override string Glyph => "\uE8C6";

    // ── Command constants ───────────────────────────────────────────────────
    private const byte CmdByte = 0x02;
    private const byte KeyGroupA = 0x04;   // 4 relays, indices 0-3
    private const byte KeyGroupB = 0x03;   // 2 relays, indices 0-1
    private const byte StateOn   = 0x01;
    private const byte StateOff  = 0x00;

    // ── Relay state (null = unknown) ────────────────────────────────────────
    private readonly bool?[] _relayState = new bool?[6]; // [A0,A1,A2,A3,B0,B1]

    // ── Connected interface ─────────────────────────────────────────────────
    private PeripheralInterface _interface;

    // ── Relay button map (index → button) ──────────────────────────────────
    private Button[] _relayButtons;

    // ── Brushes ─────────────────────────────────────────────────────────────
    private static readonly SolidColorBrush BrushOn      = new(Color.FromRgb(0x28, 0xA7, 0x45));
    private static readonly SolidColorBrush BrushOff     = new(Color.FromRgb(0xCC, 0x33, 0x33));
    private static readonly SolidColorBrush BrushUnknown = new(Color.FromRgb(0x60, 0x60, 0x60));

    public RelayControlPage()
    {
        InitializeComponent();

        _relayButtons = [RelayA0Button, RelayA1Button, RelayA2Button, RelayA3Button, RelayB0Button, RelayB1Button];

        ConnectButton.Click    += (_, _) => ConnectToDevice();
        DisconnectButton.Click += (_, _) => DisconnectDevice();
        AllOnButton.Click      += (_, _) => SetAllRelays(true);
        AllOffButton.Click     += (_, _) => SetAllRelays(false);

        RefreshAllButtonColors();
    }

    // =========================================================================
    // CONNECTION
    // =========================================================================

    private void ConnectToDevice()
    {
        DisconnectDevice();

        ushort pid       = ParseHex16(PidTextBox.Text);
        ushort usagePage = ParseHex16(UsagePageTextBox.Text);

        if (pid == 0)
        {
            SetStatus("Invalid PID. Enter a valid hex value (e.g. 079B).");
            return;
        }

        SetStatus("Scanning for device…");

        // Search through already-discovered devices; no DeviceSelection connection is made.
        IPeripheralDetail target = null;
        try
        {
            var discovered = DeviceSelection.Instance.DiscoveredDevices;
            foreach (var dev in discovered)
            {
                if (dev.PID != pid) continue;
                target = dev.interfaces.FirstOrDefault(i =>
                    usagePage == 0 || i.UsagePage == usagePage);
                if (target != null) break;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Device enumeration failed: {ex.Message}");
            return;
        }

        if (target == null)
        {
            SetStatus($"No device found with PID=0x{pid:X4}, UsagePage=0x{usagePage:X4}.");
            return;
        }

        try
        {
            _interface = target.Connect(false);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to open interface: {ex.Message}");
            return;
        }

        if (_interface == null)
        {
            SetStatus("Failed to open interface (null returned).");
            return;
        }

        // Reset relay state to unknown since we just connected.
        for (int i = 0; i < _relayState.Length; i++)
            _relayState[i] = null;
        RefreshAllButtonColors();

        SetStatus($"Connected: PID=0x{target.PID:X4}, UsagePage=0x{target.UsagePage:X4}, Usage=0x{target.Usage:X4}.");
        ConnectButton.IsEnabled    = false;
        DisconnectButton.IsEnabled = true;
    }

    private void DisconnectDevice()
    {
        _interface = null;

        Dispatcher.Invoke(() =>
        {
            ConnectButton.IsEnabled    = true;
            DisconnectButton.IsEnabled = false;
        });

        for (int i = 0; i < _relayState.Length; i++)
            _relayState[i] = null;

        RefreshAllButtonColors();
        SetStatus("Disconnected.");
    }

    // =========================================================================
    // RELAY BUTTON CLICKS
    // =========================================================================

    private async void RelayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        string tag = btn.Tag as string ?? string.Empty;

        (byte key, byte index, int stateArrayIndex) = ParseRelayTag(tag);
        if (key == 0) return;

        // Toggle current state; default to ON if unknown.
        bool currentState = _relayState[stateArrayIndex] ?? false;
        bool newState = !currentState;

        await SendRelayCommandAsync(key, index, newState);
        _relayState[stateArrayIndex] = newState;
        RefreshButtonColor(stateArrayIndex);
    }

    // =========================================================================
    // ALL ON / ALL OFF
    // =========================================================================

    private async void SetAllRelays(bool on)
    {
        // Group A: Key 4, indices 0-3
        for (byte i = 0; i < 4; i++)
        {
            await SendRelayCommandAsync(KeyGroupA, i, on);
            _relayState[i] = on;
            RefreshButtonColor(i);
        }
        // Group B: Key 3, indices 0-1
        for (byte i = 0; i < 2; i++)
        {
            await SendRelayCommandAsync(KeyGroupB, i, on);
            _relayState[4 + i] = on;
            RefreshButtonColor(4 + i);
        }
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>Turn ON a relay by key and device index.
    /// Key: <see cref="KeyGroupA"/> (0x04) indices 0–3, or <see cref="KeyGroupB"/> (0x03) indices 0–1.</summary>
    [POST(requireMainThread:true)]
    public async Task TurnOnRelayAsync(byte key, byte idx)
    {
        int stateIndex = KeyAndIdxToStateIndex(key, idx);
        await SendRelayCommandAsync(key, idx, true);
        _relayState[stateIndex] = true;
        Dispatcher.Invoke(() => RefreshButtonColor(stateIndex));
    }

    /// <summary>Turn OFF a relay by key and device index.
    /// Key: <see cref="KeyGroupA"/> (0x04) indices 0–3, or <see cref="KeyGroupB"/> (0x03) indices 0–1.</summary>
    [POST(requireMainThread:true)]
    public async Task TurnOffRelayAsync(byte key, byte idx)
    {
        int stateIndex = KeyAndIdxToStateIndex(key, idx);
        await SendRelayCommandAsync(key, idx, false);
        _relayState[stateIndex] = false;
        Dispatcher.Invoke(() => RefreshButtonColor(stateIndex));
    }

    // =========================================================================
    // SEND HELPER
    // =========================================================================

    private async Task SendRelayCommandAsync(byte key, byte index, bool on)
    {
        if (_interface == null || !_interface.IsDeviceConnected)
        {
            SetStatus("Not connected. Click Connect first.");
            return;
        }

        byte[] cmd = [CmdByte, key, index, 0x00, on ? StateOn : StateOff];

        try
        {
            await _interface.Write(cmd).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetStatus($"Send error: {ex.Message}");
        }
    }

    // =========================================================================
    // UI HELPERS
    // =========================================================================

    private void RefreshAllButtonColors()
    {
        for (int i = 0; i < _relayButtons.Length; i++)
            RefreshButtonColor(i);
    }

    private void RefreshButtonColor(int index)
    {
        if (index < 0 || index >= _relayButtons.Length) return;

        Dispatcher.Invoke(() =>
        {
            var btn = _relayButtons[index];
            bool? state = _relayState[index];

            btn.Background = state switch
            {
                true  => BrushOn,
                false => BrushOff,
                null  => BrushUnknown,
            };

            // Update button label to include ON/OFF status
            string tag = btn.Tag as string ?? string.Empty;
            btn.Content = state switch
            {
                true  => $"{tag}\nON",
                false => $"{tag}\nOFF",
                null  => tag,
            };
        });
    }

    private void SetStatus(string text)
    {
        try { Dispatcher.Invoke(() => StatusTextBlock.Text = text); } catch { }
    }

    // =========================================================================
    // PARSING HELPERS
    // =========================================================================

    /// <summary>Parse a relay tag like "A0", "A3", "B0", "B1" into (key, index, stateArrayIndex).</summary>
    private static (byte key, byte index, int stateIndex) ParseRelayTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Length < 2)
            return (0, 0, 0);

        char group = char.ToUpperInvariant(tag[0]);
        if (!byte.TryParse(tag[1..], out byte idx))
            return (0, 0, 0);

        return group switch
        {
            'A' when idx <= 3 => (KeyGroupA, idx, idx),
            'B' when idx <= 1 => (KeyGroupB, idx, 4 + idx),
            _                  => (0, 0, 0),
        };
    }

    /// <summary>Convert a flat relay index (0–5) to (key, deviceIndex).</summary>
    private static (byte key, byte deviceIndex) RelayIndexToKeyAndIdx(int relayIndex)
    {
        return relayIndex switch
        {
            0 => (KeyGroupA, 0),
            1 => (KeyGroupA, 1),
            2 => (KeyGroupA, 2),
            3 => (KeyGroupA, 3),
            4 => (KeyGroupB, 0),
            5 => (KeyGroupB, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(relayIndex)),
        };
    }

    private static ushort ParseHex16(string text)
    {
        text = (text ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : (ushort)0;
    }

    /// <summary>Convert a (key, deviceIndex) pair to the flat state-array index (0–5).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for an unknown key/index combination.</exception>
    private static int KeyAndIdxToStateIndex(byte key, byte idx)
    {
        return (key, idx) switch
        {
            (KeyGroupA, 0) => 0,
            (KeyGroupA, 1) => 1,
            (KeyGroupA, 2) => 2,
            (KeyGroupA, 3) => 3,
            (KeyGroupB, 0) => 4,
            (KeyGroupB, 1) => 5,
            _ => throw new ArgumentOutOfRangeException($"Unknown key=0x{key:X2}, idx={idx}."),
        };
    }
}
