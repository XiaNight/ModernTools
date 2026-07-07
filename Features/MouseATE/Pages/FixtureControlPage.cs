using Base.Core;
using Base.Pages;

namespace MouseATE.Pages;

[PageInfo("Fixture Control",
    Glyph = "",
    Description = "Connect and manually control the JTB500+TC100 or JTHS300 robot arm fixture.",
    NavOrder = 100,
    Path = ["ATE"],
    ShowDeviceSelection = false)]
public class FixtureControlPage : PageBase
{

    private FixtureControlView _view;

    public override void Awake()
    {
        base.Awake();
        _view = new FixtureControlView();
        root.Children.Add(_view);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        _view?.Arm?.Disconnect();
    }
}
