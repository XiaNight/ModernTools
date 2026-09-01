using Base.Core;
using Base.Pages;
using System.Windows.Controls;

namespace ATE;
/// <summary>
/// Interaction logic for DtmPage.xaml
/// </summary>

[PageInfo("DTM", NavOrder = 0, Glyph = "\xEC05", Path = ["ATE"])]
public partial class DtmPage : PageBase
{
    public DtmPage()
    {
        InitializeComponent();
    }
}
