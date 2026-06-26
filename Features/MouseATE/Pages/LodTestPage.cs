using Base.Core;
using Base.Pages;

namespace MouseATE.Pages;

[PageInfo("LOD Test",
    Glyph = "",
    Description = "Measure mouse Lift-Off Distance by stepping Z height and checking circular tracking.",
    NavOrder = 103,
    Path = ["ATE", "Mouse"],
    ShowDeviceSelection = false)]
public class LodTestPage : PageBase
{

    private LodTestView _view;
    private Tests.RawInputCapture _capture;

    public override void Awake()
    {
        base.Awake();
        _view = new LodTestView();
        root.Children.Add(_view);
    }

    public override void Start()
    {
        base.Start();
        _capture = new Tests.RawInputCapture(Main.Handle);
        Main.WindowMessageReceived += OnWindowMessage;
        _view.SetCapture(_capture);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _view.SetArm(AteSession.Arm);
    }

    private void OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, bool handled)
    {
        _capture?.OnWndProc((IntPtr)hwnd, msg, (IntPtr)wParam, (IntPtr)lParam);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Main.WindowMessageReceived -= OnWindowMessage;
    }
}
