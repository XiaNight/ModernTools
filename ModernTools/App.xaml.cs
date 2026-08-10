using Base.Core;
using Base.Helpers;
using Base.UI.Themes;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace ModernTools;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ComSecurityHelper.Initialize();
        base.OnStartup(e);

        string app_name = Util.GetAssemblyAttribute<AssemblyProductAttribute>(a => a.Product);

        LocalAppDataStore.Init("ASUS", app_name);

        // Apply the saved appearance (theme + accent) before the window is created so there is no
        // light-frame flash. ThemeService re-applies later once the window exists.
        ThemeController.Apply(ThemeController.LoadSaved());

        var window = new Base.MainWindow();
        window.Show();
    }
}