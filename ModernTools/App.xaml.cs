using Audio;
using Base.Helpers;
using Gamepad;
using KeyboardHallSensor;
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

        typeof(AudioPage).ToString();
        typeof(GamepadPage).ToString();
        typeof(RawPage).ToString();

        var window = new Base.MainWindow();
        window.Show();
    }
}