# ModernTools

A Windows desktop diagnostic app for **ROG gaming peripheral developers** — mice, keyboards, headsets, and gamepads. It surfaces rich, clear device information so developers can diagnose in-development peripherals. It is not ASUS-only: it also shows basic HID information for other brands.

## Architecture

The solution is a thin shell plus plugins. `ModernTools/` is the startup EXE that hosts the app. `Base/` is the shared framework library — the window shell, page foundation, navigation, theming, and device/protocol infrastructure. Each folder under `Features/` is a self-contained plugin that covers one aspect of peripherals and is discovered by reflection at runtime; there is no manual registration.

Detail lives close to the code. Read `Base/CLAUDE.md` before touching framework-level things, and read the `CLAUDE.md` inside a feature folder before working in that feature.

## Rules

**Base is common-only, and edits need approval.** `Base/` holds only functionality shared across features. Before changing anything in `Base/`, stop and ask me for approval — explain what you need and why. Feature-specific logic belongs in the feature, never in Base.

**Each feature owns its own code.** Keep a feature's pages, controls, protocols, and assets inside its own folder. Don't reach across features except through the shared references that already exist.

**Theme correctly.** The app supports runtime-interchangeable light and dark themes driven by ModernWpf, with a swappable accent (ROG red / anniversary gold). Use ModernWpf's `DynamicResource` theme brushes for surfaces, text, and backgrounds (e.g. `SystemControlBackgroundChromeMediumLowBrush`, `SystemControlForegroundBaseHighBrush`, `ApplicationPageBackgroundThemeBrush`, and `SystemControlForegroundAccentBrush` for the accent). The only project-defined brushes are the categorical accents `Accent1Brush`–`Accent4Brush` in `Base/UI/Themes/Brushes.xaml`. Never hard-code colors or use `StaticResource` for themed values — that breaks live theme switching. See `Base/CLAUDE.md` for details.

**Pages derive from `PageBase`.** A page is a `Base.Pages.PageBase` subclass decorated with `[PageInfo(...)]`, which places it in the navigation. Use the built-in lifecycle hooks (`Awake`, `Start`, `OnEnable`/`OnDisable`, `Update`, `ThemeChanged`, `OnApplicationQuit`) rather than wiring your own. Follow the existing code-behind style with `RelayCommand`; there is no MVVM layer. Details in `Base/CLAUDE.md`.

## Conventions

Target `net8.0-windows` / C# 12, `<Nullable>disable</Nullable>`. Formatting is enforced by `.editorconfig`: **tabs**, Allman braces, file-scoped namespaces, explicit types (no `var`), CRLF, no final newline. Match it.

## Building & debugging

Build with `dotnet build` (build a single project to avoid `bin`/`obj` lock clashes if Visual Studio is open). You may launch and debug the app to verify your changes. Note it opens a GUI window and some features need real peripherals attached, so hardware-dependent behavior may not exercise fully without a device. Release packaging is handled by `publish.ps1`.
