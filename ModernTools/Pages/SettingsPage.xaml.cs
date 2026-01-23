using System.Windows;
using System.Windows.Controls;
using ModernWpf;

namespace ModernTools;

/// <summary>
/// Interaction logic for SettingsPage.xaml
/// </summary>
public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        UpdateThemeButtons();
    }

    private void LightTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
        UpdateThemeButtons();
    }

    private void DarkTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
        UpdateThemeButtons();
    }

    private void UpdateThemeButtons()
    {
        var currentTheme = ThemeManager.Current.ApplicationTheme;
        LightThemeButton.Style = currentTheme == ApplicationTheme.Light 
            ? (Style)Application.Current.FindResource("AccentButtonStyle")
            : null;
        DarkThemeButton.Style = currentTheme == ApplicationTheme.Dark 
            ? (Style)Application.Current.FindResource("AccentButtonStyle")
            : null;
    }
}
