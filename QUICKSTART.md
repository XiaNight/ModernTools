# Quick Start Guide

## Get Up and Running in 5 Minutes!

### Step 1: Clone the Repository
```bash
git clone https://github.com/XiaNight/ModernTools.git
cd ModernTools
```

### Step 2: Open in Visual Studio (Windows Required)
- **Option A**: Double-click `ModernTools.slnx` in Windows Explorer
- **Option B**: Open Visual Studio → File → Open → Solution → Select `ModernTools.slnx`

### Step 3: Build the Solution
- Press `Ctrl+Shift+B` or
- Click Build → Build Solution

### Step 4: Run the Application
- Press `F5` (with debugging) or `Ctrl+F5` (without debugging)
- The application window will appear!

## What You'll See

When the application launches, you'll see:

```
┌─────────────────────────────────────────────┐
│ File  Edit  View  Help    ModernTools      │
├──────┬──────────────────────────────────────┤
│  ☰   │                                      │
│ Menu │      Welcome to ModernTools!         │
│      │                                      │
│  🏠  │   Developer Tool Base Plate          │
│ Home │                                      │
│      │           Features:                  │
│  🔧  │  ◈ Modern WPF UI with themes         │
│ Tools│  ◈ Collapsible navigation            │
│      │  ◈ Integrated log window             │
│  ⚙   │  ◈ Extensible framework              │
│ Sett │                                      │
├──────┴──────────────────────────────────────┤
│ Output Log                      [Clear] [▼] │
│ [02:12:05] App initialized successfully     │
└─────────────────────────────────────────────┘
```

## Try These Features

### 1. Toggle Navigation Panel
- Click the **☰ Menu** button at the top-left
- Watch the panel collapse to show icons only
- Click again to expand back to full width

### 2. Switch Between Pages
- Click **🏠 Home** to see the welcome page
- Click **🔧 Tools** to see sample tool cards
- Click **⚙ Settings** to configure the app

### 3. Change Theme
- **Method 1**: Click View → Toggle Theme in menu bar
- **Method 2**: Go to Settings → Click Light or Dark button
- Watch the entire UI change instantly!

### 4. Use the Log Window
- Watch messages appear as you navigate
- Click **Clear** to reset the log
- Click **▼** to hide/show the log window

## Next Steps: Build Your First Tool

### 1. Create a New Page

Create `ModernTools/Pages/MyToolPage.xaml`:
```xml
<Page x:Class="ModernTools.MyToolPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ui="http://schemas.modernwpf.com/2019"
      Title="MyToolPage">
    <Grid Margin="24">
        <ui:SimpleStackPanel Spacing="16">
            <TextBlock Text="My Custom Tool" 
                      FontSize="28" 
                      FontWeight="Bold"/>
            
            <TextBlock Text="Add your tool functionality here!"/>
            
            <Button Content="Click Me!" 
                   Style="{StaticResource AccentButtonStyle}"
                   Click="DoSomething_Click"/>
        </ui:SimpleStackPanel>
    </Grid>
</Page>
```

Create `ModernTools/Pages/MyToolPage.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;

namespace ModernTools;

public partial class MyToolPage : Page
{
    public MyToolPage()
    {
        InitializeComponent();
    }

    private void DoSomething_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Hello from My Tool!");
    }
}
```

### 2. Add Navigation Button

In `MainWindow.xaml`, add this inside the navigation StackPanel:
```xml
<Button Style="{StaticResource NavButtonStyle}"
        Click="NavigateMyTool_Click"
        ToolTip="My Tool">
    <StackPanel Orientation="Horizontal">
        <ui:FontIcon Glyph="&#xE8F1;" FontSize="16"/>
        <TextBlock x:Name="NavMyTool" 
                 Text="My Tool" 
                 Margin="12,0,0,0"
                 VerticalAlignment="Center"/>
    </StackPanel>
</Button>
```

### 3. Add Click Handler

In `MainWindow.xaml.cs`, add this method:
```csharp
private void NavigateMyTool_Click(object sender, RoutedEventArgs e)
{
    ContentFrame.Navigate(new MyToolPage());
    LogMessage("Navigated to My Tool page.");
}
```

Also update the toggle method to handle the new nav item:
```csharp
private void ToggleNav_Click(object sender, RoutedEventArgs e)
{
    isNavExpanded = !isNavExpanded;
    
    NavToggleText.Visibility = isNavExpanded ? Visibility.Visible : Visibility.Collapsed;
    NavHome.Visibility = isNavExpanded ? Visibility.Visible : Visibility.Collapsed;
    NavTools.Visibility = isNavExpanded ? Visibility.Visible : Visibility.Collapsed;
    NavSettings.Visibility = isNavExpanded ? Visibility.Visible : Visibility.Collapsed;
    NavMyTool.Visibility = isNavExpanded ? Visibility.Visible : Visibility.Collapsed; // Add this line
    
    LogMessage($"Navigation panel {(isNavExpanded ? "expanded" : "contracted")}.");
}
```

### 4. Build and Run

- Press `F5` to build and run
- You'll see your new "My Tool" in the navigation
- Click it to navigate to your tool page
- Click the button to see your message!

## Common Questions

### Q: Can I run this on Mac/Linux?
**A**: No, WPF is Windows-only. You would need Windows (physical or VM) to run this application.

### Q: Can I change the colors/fonts?
**A**: Yes! ModernWPF uses resource dictionaries. You can override colors and styles in `App.xaml`.

### Q: How do I add icons?
**A**: Use `<ui:FontIcon Glyph="&#xE8F1;"/>` with Segoe MDL2 Assets icon codes. Find codes here: https://docs.microsoft.com/en-us/windows/apps/design/style/segoe-ui-symbol-font

### Q: Can I add database/network features?
**A**: Absolutely! Add NuGet packages for Entity Framework, HttpClient, etc. This is just the UI base plate.

### Q: How do I deploy my app?
**A**: Use `dotnet publish` or Visual Studio's Publish feature to create a standalone .exe.

## Resources

- **Full Documentation**: See README.md
- **Usage Guide**: See USAGE.md
- **UI Documentation**: See SCREENSHOTS.md
- **Implementation Details**: See IMPLEMENTATION_SUMMARY.md

## Need Help?

- Check the documentation files in the repository
- Look at the existing pages for examples (HomePage, ToolsPage, SettingsPage)
- Study the MainWindow.xaml for layout patterns

## Happy Coding! 🚀

You now have a solid foundation to build any developer tool you can imagine!

---

**Tip**: Start simple! Add one feature at a time, test it, then move to the next. The base plate handles all the UI framework, so you can focus on your tool's functionality.
