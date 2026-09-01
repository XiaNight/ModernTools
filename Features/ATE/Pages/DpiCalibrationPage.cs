using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;

namespace MouseATE.Pages;

[PageInfo("DPI Calibration",
    Glyph = "",
    Description = "Automated DPI calibration pipelines (ReCalibration / CheckCalibration) with HID factory commands.",
    NavOrder = 102,
    Path = ["ATE", "Mouse"],
    ShowDeviceSelection = true)]
public class DpiCalibrationPage : PageBase
{

    private DpiCalibrationView _view;
    private Tests.RawInputCapture _capture;

    public override void Awake()
    {
        base.Awake();
        _view = new DpiCalibrationView();
        root.Children.Add(_view);
    }

    public override void Start()
    {
        base.Start();
        _capture = new Tests.RawInputCapture(Main.Handle);
        Main.WindowMessageReceived += OnWindowMessage;
        _view.SetCapture(_capture);

        DeviceSelection.Instance.OnActiveDeviceConnected += ConnectInterface;
        DeviceSelection.Instance.OnActiveDeviceDisconnected += DisconnectInterface;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _view.SetArm(AteSession.Arm);
        if (DeviceSelection.Instance.ActiveDevice != null)
            ConnectInterface();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Main.WindowMessageReceived -= OnWindowMessage;
    }

    private void ConnectInterface()
    {
        var device = DeviceSelection.Instance.ActiveDevice;
        if (device == null || device.interfaces.Count == 0) return;
        try
        {
            var iface = device.interfaces[0].Connect(true);
            _view.ActiveInterface = iface;
        }
        catch (Exception ex)
        {
            Debug.Log($"[MouseATE] Failed to open HID device: {ex.Message}");
        }
    }

    private void DisconnectInterface() => _view.ActiveInterface = null;

    private void OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, bool handled)
        => _capture?.OnWndProc((IntPtr)hwnd, msg, (IntPtr)wParam, (IntPtr)lParam);
}
