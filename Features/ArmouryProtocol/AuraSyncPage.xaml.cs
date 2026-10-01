using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace ArmouryProtocol;
/// <summary>
/// Interaction logic for AuraSyncPage.xaml
/// </summary>
[PageInfo("Aura Sync", Path = ["Keyboard", "Armoury"])]
public partial class AuraSyncPage : PageBase, INotifyPropertyChanged
{
    PeripheralInterface ActiveInterface;
    private volatile bool commandPending;

    private bool isAuraSyncActive;

    private byte[] startAuraSyncA = [0x51, 0x2C, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00];
    private byte[] startAuraSyncB = [0x74, 0x00, 0x00, 0x00, 0x01 ];

    private byte[] arrayBuffer = new byte[64];

    private int currentIndex = 0;
    private DispatcherTimer timer;

    public bool IsAuraSyncActive
    {
        get => isAuraSyncActive;
        set
        {
            if (isAuraSyncActive == value)
                return;

            isAuraSyncActive = value;
            OnPropertyChanged();
        }
    }

    public AuraSyncPage()
    {
        InitializeComponent();
        DataContext = this;
        timer = new(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMicroseconds(100),
        };
        timer.Tick += SendNext;
    }

    public override void Start()
    {
        base.Start();

        DeviceSelection.Instance.OnActiveDeviceConnected += OnActiveDeviceConnected;
        DeviceSelection.Instance.OnActiveDeviceDisconnected += OnActiveDeviceDisconnected;
    }

    private void OnActiveDeviceConnected()
    {
        var device = DeviceSelection.Instance.ActiveDevice;
        if (device == null) return;
        try
        {
            var usagePage = device.PID == 0x1ACE ? 0xFF02 : 0xFF00;
            if (device.interfaces.Count == 0) return;

            var deviceInterface = device.interfaces.FirstOrDefault(@interface =>
                (@interface.UsagePage == usagePage) && (@interface.Usage == 1),
                device.interfaces[0]
            );
            if (deviceInterface == null) return;

            ActiveInterface = deviceInterface.Connect(true);
            ActiveInterface.OnDataReceived += ActiveInterface_OnDataReceived;
        }
        catch (Exception ex)
        {
            Debug.Log("[Keyboard] Failed to open HID device: " + ex.Message);
            return;
        }
    }

    private void OnActiveDeviceDisconnected()
    {
        IsAuraSyncActive = false;
        timer.Stop();
        if (ActiveInterface != null)
        {
            ActiveInterface.OnDataReceived -= ActiveInterface_OnDataReceived;
            ActiveInterface.Dispose();
            ActiveInterface = null;
        }
    }

    private void ActiveInterface_OnDataReceived(ReadOnlyMemory<byte> bytes, DateTime time)
    {
        ReadOnlySpan<byte> span = bytes.Span;

        if(span.Length < 3) return;
        if (span[1] != 0xC0 || span[2] != 0x82) return;

        commandPending = false;
    }

    private void StartButtonClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ActiveInterface == null) return;
        IsAuraSyncActive = true;

        ProtocolService.AppendCmd(ActiveInterface, startAuraSyncA);
        ProtocolService.AppendCmd(ActiveInterface, startAuraSyncB);

        currentIndex = 0;
        commandPending = false;
        timer.Start();
    }

    private void StopButtonClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        IsAuraSyncActive = false;
        timer.Stop();
        commandPending = false;
    }

    private void SendNext(object? sender, EventArgs e)
    {
        if (ActiveInterface == null) return;

        if (ProtocolService.PendingCmdCount > 5) return;

        FabricateRandomCommand(arrayBuffer);
        ProtocolService.AppendCmd(ActiveInterface, arrayBuffer);

        commandPending = true;
    }

    private byte[] FabricateRandomCommand(byte[] array)
    {
        int currentIndex = 4;
        int randomKeyCount = Random.Shared.Next(1, 10);

        for (int i = 0; i < randomKeyCount; i++)
        {
            FabricateKeyInfo(array, currentIndex);
            currentIndex += 4;
        }

        array[0] = 0xC0;
        array[1] = 0x81;
        array[2] = (byte)randomKeyCount;
        array[3] = 0x00;

        return array;
    }

    private void FabricateKeyInfo(byte[] array, int offset)
    {
        array[offset] = (byte)Random.Shared.Next(0, 256);
        array[offset + 1] = (byte)Random.Shared.Next(0, 256);
        array[offset + 2] = (byte)Random.Shared.Next(0, 256);
        array[offset + 3] = (byte)Random.Shared.Next(0, 256);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
