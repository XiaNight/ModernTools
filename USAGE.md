# ModernTools Usage Guide

## Application Overview

ModernTools is a developer tool base plate built with WPF and ModernWPF. It provides a modern, extensible framework for building custom developer tools.

## UI Layout

```
┌─────────────────────────────────────────────────────────────┐
│  File  Edit  View  Help                   (Menu Bar)        │
├────┬────────────────────────────────────────────────────────┤
│    │                                                         │
│ ☰  │                                                         │
│    │                                                         │
│ 🏠 │              Main Content Area                          │
│    │         (Page-based navigation)                         │
│ 🔧 │                                                         │
│    │                                                         │
│ ⚙  │                                                         │
│    │                                                         │
│Nav │                                                         │
│    │                                                         │
├────┴────────────────────────────────────────────────────────┤
│  Output Log                                     [Clear] [▼]  │
│  [02:12:05] Application started...                          │
│  [02:12:06] Navigated to Home page.                         │
└─────────────────────────────────────────────────────────────┘
```

## Key Features

### 1. Navigation Panel (Left Side)

**Expanded State:**
- Shows icon AND text for each navigation item
- Width: ~200px
- Items displayed:
  - ☰ Menu (Toggle button)
  - 🏠 Home
  - 🔧 Tools
  - ⚙ Settings

**Contracted State:**
- Shows icons ONLY
- Width: ~48px
- Saves screen space while maintaining quick access

**Toggle:**
- Click the Menu button (☰) at the top
- Animation smoothly transitions between states
- Preference is maintained during session

### 2. Menu Bar (Top)

**File Menu:**
- New (Ctrl+N)
- Open (Ctrl+O)
- Exit

**Edit Menu:**
- Copy (Ctrl+C)
- Paste (Ctrl+V)

**View Menu:**
- Toggle Theme (Switch between Light/Dark)
- Toggle Log Window (Show/Hide output log)

**Help Menu:**
- About

### 3. Content Area (Center)

**Page-based Navigation:**
The main content area uses a Frame control that supports page navigation.

**Available Pages:**

#### Home Page
- Welcome message
- Feature overview
- Quick start information
- Modern card-based layout

#### Tools Page
- Placeholder cards for custom tools
- Each tool card includes:
  - Icon
  - Name
  - Description
  - Launch button
- Extensible - add your own tools here

#### Settings Page
- Theme selection (Light/Dark)
- General preferences
- Application information
- About section

### 4. Output Log (Bottom)

**Features:**
- Collapsible panel (default: visible)
- Height: 200px when visible
- Timestamp for each log entry
- Auto-scrolling to latest entry
- Clear button to reset log
- Hide/Show toggle button

**Log Messages Include:**
- Application startup
- Navigation events
- Theme changes
- User actions

## Theme Support

### Light Theme
- Clean, bright interface
- Ideal for well-lit environments
- Traditional desktop appearance

### Dark Theme
- Modern, dark interface
- Reduces eye strain in low-light conditions
- Popular among developers

### Switching Themes

**Method 1: Menu Bar**
1. Click View → Toggle Theme

**Method 2: Settings Page**
1. Navigate to Settings
2. Click Light or Dark button in Appearance section

Theme preference is applied immediately to the entire application.

## Keyboard Shortcuts

- `Ctrl+N` - New (File menu)
- `Ctrl+O` - Open (File menu)
- `Ctrl+C` - Copy (Edit menu)
- `Ctrl+V` - Paste (Edit menu)

## Extending the Application

### Adding New Pages

1. Create XAML and code-behind in `Pages/` folder
2. Add navigation button to `MainWindow.xaml`
3. Implement click handler in `MainWindow.xaml.cs`
4. Use the Frame navigation system

### Adding Tools

1. Create new page for complex tools
2. Add tool card to Tools page for quick access
3. Implement tool functionality
4. Log important events to output window

### Customizing Appearance

- ModernWPF provides consistent styling
- Override resources in App.xaml for global changes
- Use `ui:ControlHelper` for per-control customization
- Colors and brushes adapt to theme automatically

## Best Practices

1. **Use the Log Window**: Log important events for debugging and user feedback
2. **Maintain Navigation**: Keep navigation items concise and organized
3. **Theme Compatibility**: Test UI in both light and dark themes
4. **Responsive Design**: Ensure tools work at various window sizes
5. **User Preferences**: Remember user choices (nav state, theme, etc.)

## Technical Details

- **Framework**: .NET 8.0
- **UI Library**: WPF with ModernWPF
- **Theme System**: ModernWPF ThemeManager
- **Navigation**: Frame-based page navigation
- **Minimum Window Size**: 900x600

## Common Tasks

### Task: Hide the Log Window
1. Click View → Toggle Log Window
2. Or click the ▼ button in log header

### Task: Collapse Navigation
1. Click the Menu (☰) button
2. Navigation shows icons only

### Task: Change Theme
1. Click View → Toggle Theme
2. Or go to Settings page and select theme

### Task: Clear Log History
1. Click the Clear button in log header
2. Keeps the window open

### Task: Navigate Between Pages
1. Click any icon/label in left navigation
2. Content updates in main area
3. Navigation state logged

## Support and Development

For questions, issues, or contributions:
- Repository: https://github.com/XiaNight/ModernTools
- Documentation: See README.md
- Examples: Check Pages/ folder for implementation patterns

## Version History

**v1.0.0** (Initial Release)
- Basic layout with navigation panel
- Three sample pages (Home, Tools, Settings)
- Light/Dark theme support
- Output log window
- Menu bar with common actions
