# Base — shared framework

`Base` is the framework every feature builds on: the shell window, page foundation, navigation, theming, and device/protocol infrastructure. It contains **only** things shared across features.

> **Editing Base requires my approval.** Ask before changing anything here, and say what you need and why. If something is specific to one feature, it belongs in that feature, not in Base.

## Page foundation — `PageBase`

`Base/UI/Pages/PageBase.cs` — `public abstract class PageBase : WpfBehaviour, IPageBase`. Every feature page derives from it. The lifecycle itself lives on `WpfBehaviour` (`Base/Core/WpfBehaviour.cs`, a `UserControl`), which gives pages a Unity-style lifecycle. Override only what you need:

- `Awake()` — once, when the page is registered. Loads `[Persist]` fields; `PageBase` builds its root `Grid` here.
- `Start()` — once, on first enable.
- `OnEnable()` / `OnDisable()` — each time the page is shown/hidden.
- `Update()` — per-frame while enabled (driven by `CompositionTarget.Rendering`). Use for live device visualization; keep it cheap.
- `ThemeChanged()` — on light/dark toggle. Refresh anything you drew with fixed colors here.
- `OnApplicationQuit(CancelEventArgs)` — saves `[Persist]` fields. `OnDestroy()` for teardown.

`Enable()`/`Disable()` and `IsEnabled` orchestrate the above; a `WpfBehaviour` self-registers with the shell in its constructor, so you don't wire it up manually. `WpfBehaviourSingleton<T>` is a self-registering lazy singleton preloaded at startup.

`PageBase` also exposes `protected static DeviceSelection.Device ActiveDevice`, `protected Grid root`, and page metadata read from its `[PageInfo]`.

## Navigation — `[PageInfo]`

Pages appear in navigation purely by being a concrete `PageBase` decorated with `[PageInfo]` (`Base/Framework/Attributes/PageInfoAttribute.cs`). The shell (`Base/MainWindow.xaml.cs`) loads plugin DLLs, scans assemblies by reflection, reads `[PageInfo]`, and builds the nav tabs — **no registration call**. Pages are instantiated lazily on first click.

`[PageInfo]` fields: `PageName` (ctor arg), `Glyph` / `SecondaryGlyph` (Segoe Fluent), `ShortName`, `Description`, `Path` (`string[]` for nested nav grouping, e.g. `["Keyboard", "Hall Effect"]`), `NavOrder` (default `int.MaxValue`; negative hides the tab), `NavAlignment` (0 = top, 1 = bottom), `ShowDeviceSelection` (default true).

Example:
```csharp
[PageInfo("Generic Mouse Analyzer", Path = ["Mouse"])]
public class GenericMouseAnalyzerPage : Base.Pages.PageBase { ... }
```

## Theming

Runtime-swappable light/dark plus accent, driven by ModernWpf. The user's choice is a `ThemeMode` (`Light`, `Dark`, `BlackGold`, `Auto`) exposed as a `[Setting]` on the `ThemeService` singleton and shown on the Settings page. `ThemeController.Apply(mode)` maps each mode to a ModernWpf `ApplicationTheme` + `AccentColor` (ROG red / anniversary gold / `null` = follow Windows) and is called at startup (`App.OnStartup`) and whenever the setting changes. `Auto` sets both to `null`, so ModernWpf tracks the Windows light/dark and accent colour live. After any change, `MainWindow.OnThemeChangedExternally()` repaints the window frame and calls `ThemeChanged()` on all behaviours; the same runs from the `ActualApplicationThemeChanged` / `ActualAccentColorChanged` events (subscribed once). The title-bar `ToggleTheme_Click` just flips Light/Dark via `ThemeService.SetMode`.

Use ModernWpf's own `DynamicResource` theme brushes for all surfaces, text, and backgrounds — they follow both light/dark and the active accent automatically. Common keys:

- Surfaces / cards: `SystemControlBackgroundChromeMediumLowBrush`, borders `SystemControlForegroundBaseLowBrush`.
- Page background: `ApplicationPageBackgroundThemeBrush`.
- Text: `SystemControlForegroundBaseHighBrush` (primary), `SystemControlForegroundBaseMediumBrush` (secondary). Note: the WinUI-era `TextFillColor*` keys do **not** exist in the pinned ModernWpf 0.9.6, so don't use them.
- Accent (ROG red / anniversary gold, set in `App.xaml` `ui:ThemeResources AccentColor`): `SystemControlForegroundAccentBrush`.

The only project-defined brushes live in `Base/UI/Themes/Brushes.xaml` and are the categorical accents `Accent1Brush`–`Accent4Brush` (chart series / meters), merged into the app via `App.xaml`. **Never hard-code colors or use `StaticResource` for themed values.**

Note: `Palette.Light/Dark.xaml` and `PaletteManager.cs` are legacy (no longer wired into the brush set) and inert; the old semantic brushes (`SurfaceBrush`, `TextPrimaryBrush`, …) and `Generic.xaml` were removed. Banner severity colors now live locally in `Banner.xaml`.

ModernWPF (`ModernWpfUI` 0.9.6) is the UI toolkit; XAML namespace `xmlns:ui="http://schemas.modernwpf.com/2019"`.

## Infrastructure & services

- **Devices** — `Base/Modules/DeviceSelection.cs` (`DeviceSelection.Instance.ActiveDevice`, `OnActiveDeviceConnected`); peripheral transports in `Base/Infrastructure/Peripheral/` (`HidInterface`, `UsbInterface`, `BLEInterface`, `BTInterface`, `PeripheralInterface`).
- **Logging** — `Debug` static logger in `Base/Application/Services/LogService.cs`.
- **Persistence** — `Base/Core/LocalAppDataStore.cs` (JSON store under `%AppData%\ASUS`); fields marked `[Persist]` are auto-saved/restored by the page lifecycle.
- **HTTP API** — `Base/Infrastructure/API/` auto-registers endpoints (also proxied by the `MCPServer` feature).
- **Commands** — `RelayCommand` in `Base/Framework/Commands/`.
- **Attributes** — `Base/Framework/Attributes/`: `[PageInfo]`, `[AppMenuItem]`, `[Config]`, `[Persist]`, `[CustomTag]`, `[MeasureTime]`.

## Key files

Shell/navigation `Base/MainWindow.xaml.cs` · page base `Base/UI/Pages/PageBase.cs` · lifecycle `Base/Core/WpfBehaviour.cs` · nav attribute `Base/Framework/Attributes/PageInfoAttribute.cs` · theming `Base/UI/Themes/Brushes.xaml` (accent brushes; ModernWpf provides the rest) · devices `Base/Modules/DeviceSelection.cs` · persistence `Base/Core/LocalAppDataStore.cs`.
