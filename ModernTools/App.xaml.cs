using Base.Core;
using Base.Helpers;
using ModernWpf;
using System.Reflection;
using System.Windows;

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
        ApplicationTheme theme = LocalAppDataStore.Instance.Get("Theme", ApplicationTheme.Light);
        ThemeManager.Current.ApplicationTheme = theme;

        var window = new Base.MainWindow();
        window.Show();
    }
}