using Base.Core;
using Base.Pages;

namespace MouseATE.Pages;

[PageInfo("ATE Settings",
    Glyph = "",
    Description = "Configure ATE device profiles and global test parameters.",
    NavOrder = 110,
    Path = ["ATE"],
    ShowDeviceSelection = false)]
public class AteSettingsPage : PageBase
{
    public override string PageName => "ATE Settings";

    private AteSettingsView _view;

    public override void Awake()
    {
        base.Awake();
        _view = new AteSettingsView();
        root.Children.Add(_view);
    }
}
