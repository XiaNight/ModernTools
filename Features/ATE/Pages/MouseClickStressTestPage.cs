using Base.Core;
using Base.Pages;
using Base.Services;
using MouseATE.Tests;

namespace MouseATE.Pages;

[PageInfo("Click Stress Test",
    Glyph = "",
    Description = "Stress-tests mouse button clicks via solenoid relay board — counts pass, miss, and extra events over N cycles.",
    NavOrder = 104,
    Path = ["ATE", "Mouse"],
    ShowDeviceSelection = false)]
public class MouseClickStressTestPage : PageBase
{

    private MouseClickStressTestView _view;
    private MouseHookService         _hook;

    public override void Awake()
    {
        base.Awake();
        _view = new MouseClickStressTestView();
        root.Children.Add(_view);
    }

    public override void Start()
    {
        base.Start();
        _hook = new MouseHookService();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        try
        {
            _hook.Install();
            if (!_hook.IsInstalled)
                Debug.Log("[ATE/ClickTest] Warning: mouse hook install failed.");
        }
        catch (Exception ex)
        {
            Debug.Log($"[ATE/ClickTest] Hook install error: {ex.Message}");
        }
        _view.SetHook(_hook);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        _view.StopIfRunning();
        _hook.Uninstall();
    }
}
