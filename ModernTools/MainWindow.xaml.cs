using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ModernWpf;

namespace ModernTools;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool isNavExpanded = true;
    private bool isLogVisible = true;

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize with Home page
        NavigateHome_Click(null, null);
        
        // Log initial message
        LogMessage("ModernTools initialized successfully.");
        LogMessage("Welcome to the Developer Tool Base Plate!");
    }

    private void ToggleNav_Click(object sender, RoutedEventArgs e)
    {
        isNavExpanded = !isNavExpanded;
        
        // Toggle visibility of text labels
        NavToggleText.Visibility = isNavExpanded ? Visibility.Visible : Visibility.Collapsed;
        NavHome.Visibility = isNavExpanded ? Visibility.Visible : Visibility.Collapsed;
        NavTools.Visibility = isNavExpanded ? Visibility.Visible : Visibility.Collapsed;
        NavSettings.Visibility = isNavExpanded ? Visibility.Visible : Visibility.Collapsed;
        
        LogMessage($"Navigation panel {(isNavExpanded ? "expanded" : "contracted")}.");
    }

    private void NavigateHome_Click(object? sender, RoutedEventArgs? e)
    {
        ContentFrame.Navigate(new HomePage());
        LogMessage("Navigated to Home page.");
    }

    private void NavigateTools_Click(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(new ToolsPage());
        LogMessage("Navigated to Tools page.");
    }

    private void NavigateSettings_Click(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(new SettingsPage());
        LogMessage("Navigated to Settings page.");
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var currentTheme = ThemeManager.Current.ApplicationTheme;
        ThemeManager.Current.ApplicationTheme = currentTheme == ApplicationTheme.Light 
            ? ApplicationTheme.Dark 
            : ApplicationTheme.Light;
        
        LogMessage($"Theme changed to {ThemeManager.Current.ApplicationTheme}.");
    }

    private void ToggleLog_Click(object sender, RoutedEventArgs e)
    {
        isLogVisible = !isLogVisible;
        LogPanel.Visibility = isLogVisible ? Visibility.Visible : Visibility.Collapsed;
        
        if (isLogVisible)
        {
            LogMessage("Log window shown.");
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
        LogMessage("Log cleared.");
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void LogMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogTextBox.AppendText($"[{timestamp}] {message}\n");
        LogTextBox.ScrollToEnd();
    }
}