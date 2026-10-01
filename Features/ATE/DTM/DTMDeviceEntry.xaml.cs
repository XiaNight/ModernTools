using Base.Services;
using Base.Services.Peripheral;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using static Base.Services.DeviceSelection;

namespace ATE.DTM;

/// <summary>
/// Interaction logic for DTMDeviceEntry.xaml
/// </summary>
public partial class DTMDeviceEntry : UserControl
{
    private PeripheralInterface connectedInterface;
    private DeviceSelection.Device connectedDevice;

    private CancellationTokenSource sweepCancellation;

    private readonly byte[] enableDtm = [0xFA, 0x23, 0x02, 0x00, 0x45, 0x44, 0x54, 0x4D];
    private readonly byte[] resetDtm = [0xFA, 0x23, 0x03, 0x00, 0x00, 0x00];
    private readonly byte[] phy2M = [0xFA, 0x23, 0x03, 0x00, 0x02, 0x08];
    private readonly byte[] db8 = [0xFA, 0x23, 0x03, 0x00, 0x09, 0x08];
    private readonly byte[] startTx = [0xFA, 0x23, 0x03, 0x00];
    private readonly byte[] stopDtm = [0xFA, 0x23, 0x03, 0x00, 0xC0, 0x00];
    private readonly byte[] txFrequencyShift = [0xFA, 0x23, 0x03, 0x00, 0xBF, 0x00];

    private const int MinChannel = 0;
    private const int MaxChannel = 79;
    private const int ChannelCount = MaxChannel - MinChannel + 1;

    // Entire 0 -> 79 sweep takes 1 second.
    private static readonly TimeSpan SweepDuration = TimeSpan.FromSeconds(3);

    public DTMDeviceEntry()
    {
        InitializeComponent();
    }

    private void DeviceDropdown_DropDownOpened(object sender, EventArgs e)
    {
        DeviceSelection.Instance.Refresh().ConfigureAwait(true);

        List<DeviceSelection.Device> connectedDevices =
            DeviceSelection.Instance.DiscoveredDevices;

        DeviceDropdown.Items.Clear();

        foreach (var device in connectedDevices)
        {
            DeviceDropdown.Items.Add(device);

            if (device == connectedDevice)
                DeviceDropdown.SelectedItem = device;
        }
    }

    private void DeviceDropdown_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DeviceDropdown.SelectedItem is not Device selectedDevice)
            return;

        ushort usagePage =
            (ushort)(selectedDevice.PID == 0x1ACE ? 0xFF02 : 0xFF00);

        if (selectedDevice.interfaces.Count == 0)
            return;

        PeripheralInterfaceDetail interfaceDetail = null;

        ushort[] candidates =
        [
            usagePage,
            0xFF01
        ];

        foreach (ushort candidate in candidates)
        {
            if (selectedDevice.FindInterface(
                    0x01,
                    candidate,
                    out interfaceDetail))
            {
                usagePage = candidate;
                break;
            }
        }

        if (interfaceDetail == null)
            return;

        Stop();

        if (connectedDevice != null &&
            connectedDevice != selectedDevice)
        {
            connectedDevice.Dispose();
        }

        connectedDevice = selectedDevice;
        connectedInterface = interfaceDetail.Connect(true);
    }

    private async void StartButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await Start();
    }

    private void StopButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Stop();
    }

    private async Task Start()
    {
        if (connectedInterface == null)
            return;

        if (sweepCancellation != null)
            return;

        StartButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;

        sweepCancellation = new CancellationTokenSource();
        CancellationToken token = sweepCancellation.Token;

        try
        {
            //
            // These are only performed once.
            //
            ProtocolService.AppendCmd(
                connectedInterface,
                "factory_enter");

            ProtocolService.AppendCmd(
                connectedInterface,
                enableDtm);

            while (!token.IsCancellationRequested)
            {
                Stopwatch sweepTimer = Stopwatch.StartNew();

                for (int channel = MinChannel;
                     channel <= MaxChannel;
                     channel += 2)
                {
                    token.ThrowIfCancellationRequested();

                    await StartChannel(channel, token);
                        
                    TimeSpan targetElapsed =
                        TimeSpan.FromTicks(
                            SweepDuration.Ticks *
                            (channel + 1) /
                            ChannelCount);

                    await DelayUntil(
                        sweepTimer,
                        targetElapsed,
                        token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (connectedInterface != null)
            {
                ProtocolService.AppendCmd(
                    connectedInterface,
                    stopDtm);
            }

            sweepCancellation?.Dispose();
            sweepCancellation = null;

            StartButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
        }
    }

    private async Task StartChannel(int channel, CancellationToken cancellationToken = default)
    {
        if (connectedInterface == null)
            return;

        byte[] startTxCommand = [.. startTx, .. GetTxStartBytes(channel)];

        await WriteAndReadWithTimeoutAsync(stopDtm, cancellationToken);
        await WriteAndReadWithTimeoutAsync(resetDtm, cancellationToken);
        await WriteAndReadWithTimeoutAsync(phy2M, cancellationToken);
        await WriteAndReadWithTimeoutAsync(db8, cancellationToken);
        await WriteAndReadWithTimeoutAsync(startTxCommand, cancellationToken);
    }

    private async Task WriteAndReadWithTimeoutAsync(
        byte[] command,
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCancellation.CancelAfter(TimeSpan.FromMilliseconds(10));

        try
        {
            await connectedInterface.WriteAndReadAsync(
                command,
                timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            // Expected if the device doesn't return a response within 10 ms.
        }
    }

    private void Stop()
    {
        CancellationTokenSource cancellation =
            sweepCancellation;

        sweepCancellation = null;

        cancellation?.Cancel();

        if (connectedInterface != null)
        {
            ProtocolService.AppendCmd(
                connectedInterface,
                stopDtm);
        }

        StartButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
    }

    private static async Task DelayUntil(
        Stopwatch stopwatch,
        TimeSpan targetElapsed,
        CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();

            TimeSpan remaining =
                targetElapsed - stopwatch.Elapsed;

            if (remaining <= TimeSpan.Zero)
                return;

            if (remaining > TimeSpan.FromMilliseconds(2))
            {
                await Task.Delay(
                    remaining - TimeSpan.FromMilliseconds(1),
                    token);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    public static byte[] GetTxStartBytes(int channel)
    {
        if (channel is < MinChannel or > MaxChannel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                "Channel must be 0-79.");
        }

        int dtmChannel = channel / 2;

        byte channelByte =
            (byte)(0x80 + dtmChannel);

        // 0x03 = Constant Carrier.
        return
        [
            channelByte,
            0x03
        ];
    }
}