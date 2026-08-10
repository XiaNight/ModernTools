# AiComposer

Generates full ModernTools pages from strings — **XAML** for UI, **C#** for logic — that are
compiled at runtime, **persisted**, reloaded on app restart, and registered into the navigation.
Intended to be driven by an AI, but ships with a composer page that is also a complete local test
harness, so the whole loop works without any external AI.

## Two kinds of page

- **The composer** (`UI/AiComposerPage`) is an ordinary `Base.Pages.PageBase` with `[PageInfo]` —
  static, attributed, discovered by the normal reflection scan. It manages generated pages and
  hosts the verification harness.
- **Generated pages** are dynamic. They are *not* compiled attributed types. Every generated page
  is an instance of one reusable host, `Runtime/GeneratedPageHost : PageBase`, bound to a page id.
  Many generated pages share this single CLR type, distinguished by instance.

## Definition & manifest format

A `Model/GeneratedPageDefinition` is the persisted unit:

| Field | Meaning |
|-------|---------|
| `Id` | stable GUID (`"N"` form), preserved across edits; also the storage folder name |
| `SchemaVersion` | on-disk format version (`CurrentSchemaVersion`) |
| `Title` | nav title (the `[PageInfo]` PageName equivalent) |
| `Glyph` | Segoe Fluent glyph, stored as the literal character (e.g. `` = ``) |
| `Group` | `/`-separated nav grouping (e.g. `Generated`, `Lab/Mice`); blank = top level |
| `Order` | nav order within its group |
| `CreatedUtc` / `ModifiedUtc` | timestamps |
| `Xaml` / `Csharp` | source (kept in sibling files, not in the manifest) |

## Storage location

Under the app's per-user data root (`LocalAppDataStore.RootDirectory`, i.e.
`%LocalAppData%\ASUS\<AppName>`), in `GeneratedPages\`. **One folder per page**, named by `Id`:

```
GeneratedPages\<id>\manifest.json   # metadata only (Xaml/Csharp are [JsonIgnore])
GeneratedPages\<id>\page.xaml       # the UI source
GeneratedPages\<id>\logic.cs        # the C# source
```

Human-readable on purpose: easy to diff, easy for an AI to read a prior version before editing.
The persisted source is the **single source of truth**; the compiled/instantiated page is only a
cached projection of it.

## Contracts (what generated code may touch)

- `Contracts/IHostApi` — the **curated** surface: logging, active-device identity
  (`IHostDevice`), a device-changed event, and `RunOnUi`. Nothing else. `Runtime/HostApi` is the
  implementation, one per materialized page, disposed on teardown.
- `Contracts/IGeneratedLogic { Initialize(IHostApi host, FrameworkElement root); }` — every
  generated page's C# implements this. The host instantiates the single implementing type, sets it
  as the parsed XAML's `DataContext`, and calls `Initialize`. Logic exposes `ICommand`
  (`Base.Helpers.RelayCommand<T>`) and bindable properties the XAML drives via `{Binding}`.

Generated **XAML is loose** (`XamlReader.Parse`): no code-behind, no CLR event handlers. All
interactivity flows through bindings/commands against the DataContext. A pre-seeded `ParserContext`
registers the default presentation namespace plus `x:` and the ModernWpf `ui:` namespace, so
fragments need no xmlns boilerplate. Generated XAML must theme via **`DynamicResource` + ModernWpf
theme brush keys only** (per the root CLAUDE.md) so live light/dark switching works.

## Trust model

Local developer diagnostic tool → **local trust**. Generated C# is compiled with **full Roslyn
trust** and loaded in-process; there is **no out-of-process sandbox**. The safety guardrail is a
**syntax-level denylist** (`Compilation/SyntaxDenylist`) walked before compilation, rejecting
`System.Diagnostics.Process`, arbitrary `System.IO`, reflection, `unsafe`, and P/Invoke. It is a
best-effort tripwire, not a security boundary — it errs toward rejecting. Complementing it, the
generated compilation's global usings deliberately omit those namespaces, so unqualified dangerous
types fail to resolve. `IHostApi` is the design-time guardrail that keeps generated code pointed at
supported services.

## Compilation & the collectible ALC

`Compilation/RoslynCompiler` runs the denylist, builds a `CSharpCompilation` (references = every
loaded assembly with a file location; C# 12; a small global-usings tree so logic needs no
boilerplate), emits to memory, and loads the result into a **collectible**
`GeneratedLoadContext` (`Load` returns null → shared types like `IGeneratedLogic`/`IHostApi` keep
one identity via the default context). Compiled modules are **cached by source hash** and
**reference-counted** by `GeneratedPageManager`; the ALC unloads when the last page drops it.

## The edit rule: destroy + recreate (never diff)

Editing is a **full replace** keyed by the same `Id`. On update the manager: overwrites the stored
definition, refreshes the nav entry **in place** (title/glyph update; the slot and the
`GeneratedPageHost` instance stay stable), and tells the host to `Rematerialize` — which tears down
the old logic + host API and **unloads the old collectible ALC**, then recompiles from the new
source. No diffing. Editing reads the stored source and does not require the page to be currently
loaded/materialized.

## Startup & loading strategy (must not regress startup time)

- `Runtime/GeneratedPageManager` is a `WpfBehaviourSingleton`, so it's created during the normal
  startup singleton preload. In `Start` it **only enumerates manifests and registers nav entries
  from metadata** (title, glyph, group, order). No XAML is parsed and no C# is compiled at startup.
- A page is **materialized lazily on first navigation** (`GeneratedPageHost.OnEnable`): a
  lightweight "Preparing…" placeholder shows, compilation runs **off the UI thread**, then WPF
  object creation is marshalled back to the dispatcher and swapped in. Results are cached by source
  hash.
- After the window is idle, a low-priority background task warms Roslyn once with a throwaway
  compile (`RoslynCompiler.Warmup`). Saved pages are **not** eagerly compiled — they compile one at
  a time, on first navigation, into the same cache.
- Malformed XAML or a compile/guardrail failure renders an **inline error panel**
  (`Runtime/ErrorPanel`) — a generated page can never crash the app.

## Base dependency

Registering a page instance outside the startup `[PageInfo]` scan needs a Base hook, because the
built-in lazy nav path is keyed by CLR *type* and `SelectPage` requires a private `navPageMap`
entry. Two approved public methods on `Base/MainWindow` provide it:
`RegisterDynamicPage(page, text, path, glyph, …)` → returns the `INavigationItem`, and
`UnregisterDynamicPage(page)`. Everything else lives in this feature.

## Files

`Model/GeneratedPageDefinition.cs` · `Persistence/GeneratedPageStore.cs` ·
`Contracts/IHostApi.cs` · `Contracts/IGeneratedLogic.cs` ·
`Compilation/` (`SyntaxDenylist`, `RoslynCompiler`, `GeneratedLoadContext`, `CompileOutcome`,
`XamlMaterializer`, `SourceHasher`) ·
`Runtime/` (`GeneratedPageManager`, `GeneratedPageHost`, `HostApi`, `ErrorPanel`, `BuiltInSample`) ·
`UI/AiComposerPage.xaml`(`.cs`).