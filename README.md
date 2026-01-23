# ModernTools

A modern, extensible WPF developer tool base plate built with ModernWPF.

## Features

- **Modern UI**: Clean, modern interface using the ModernWPF library
- **Light/Dark Mode**: Easily switch between light and dark themes
- **Collapsible Navigation**: Left-side navigation panel that can be expanded (showing icons + labels) or contracted (showing icons only)
- **Top Menu Bar**: Standard menu bar for File, Edit, View, and Help menus
- **Pull-out Log Window**: Bottom log window that can be shown or hidden
- **Page-based Navigation**: Easy-to-extend page system for building tools

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- Windows OS (WPF is Windows-only)
- Visual Studio 2022 or later (recommended) or any .NET IDE

### Building the Project

1. Clone the repository:
   ```bash
   git clone https://github.com/XiaNight/ModernTools.git
   cd ModernTools
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build the project:
   ```bash
   dotnet build
   ```

4. Run the application:
   ```bash
   dotnet run --project ModernTools/ModernTools.csproj
   ```

## Usage

### Navigation

- Click the **Menu** button at the top of the left panel to collapse/expand the navigation
- Click on any page icon/label to navigate to that page:
  - **Home**: Welcome page with feature overview
  - **Tools**: Page for your custom developer tools
  - **Settings**: Application settings and theme switcher

### Theme Switching

- Use the **View** → **Toggle Theme** menu option
- Or go to the **Settings** page and click the Light/Dark theme buttons

### Log Window

- Toggle the log window using **View** → **Toggle Log Window**
- View application events and messages in real-time
- Click **Clear** to clear the log

## Extending the Application

### Adding New Pages

1. Create a new Page in the `Pages` folder:

```xml
<Page x:Class="ModernTools.MyNewPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ui="http://schemas.modernwpf.com/2019"
      Title="MyNewPage">
    <Grid>
        <!-- Your content here -->
    </Grid>
</Page>
```

2. Add the code-behind:

```csharp
namespace ModernTools;

public partial class MyNewPage : Page
{
    public MyNewPage()
    {
        InitializeComponent();
    }
}
```

3. Add a navigation button in `MainWindow.xaml`:

```xml
<Button Style="{StaticResource NavButtonStyle}"
        Click="NavigateMyPage_Click"
        ToolTip="My Page">
    <StackPanel Orientation="Horizontal">
        <ui:FontIcon Glyph="&#xE8F1;" FontSize="16"/>
        <TextBlock x:Name="NavMyPage" 
                 Text="My Page" 
                 Margin="12,0,0,0"
                 VerticalAlignment="Center"/>
    </StackPanel>
</Button>
```

4. Add the click handler in `MainWindow.xaml.cs`:

```csharp
private void NavigateMyPage_Click(object sender, RoutedEventArgs e)
{
    ContentFrame.Navigate(new MyNewPage());
    LogMessage("Navigated to My Page.");
}
```

### Adding Tools

The Tools page is a placeholder for your custom developer tools. You can:

1. Add tool cards to the Tools page
2. Create dedicated pages for complex tools
3. Implement tool functionality in the code-behind

## Project Structure

```
ModernTools/
├── ModernTools.slnx          # Solution file
└── ModernTools/
    ├── App.xaml              # Application definition
    ├── App.xaml.cs           # Application code-behind
    ├── MainWindow.xaml       # Main window layout
    ├── MainWindow.xaml.cs    # Main window logic
    ├── ModernTools.csproj    # Project file
    └── Pages/
        ├── HomePage.xaml     # Home page
        ├── ToolsPage.xaml    # Tools page
        └── SettingsPage.xaml # Settings page
```

## Technologies Used

- **.NET 8.0**: Modern .NET framework
- **WPF**: Windows Presentation Foundation for rich desktop applications
- **ModernWPF**: Modern UI controls and themes (v0.9.6)

## License

This project is open source and available under the MIT License.

## Contributing

Contributions are welcome! Feel free to submit issues and pull requests.