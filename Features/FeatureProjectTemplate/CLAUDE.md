# FeatureProjectTemplate

The canonical starting point for a new feature plugin. Copy this project to create a feature; it establishes the plugin contract the shell relies on.

Contract (see the `.csproj`): `OutputType=Library`, `net8.0-windows`, `Nullable=disable`, `UseWPF=true`, `EnableDynamicLoading=true`, and a `Base` project reference with `Private=false` + `ExcludeAssets=runtime` so the plugin does **not** copy Base's DLLs (the shell provides them).

To add a feature: copy this folder, rename the project, add it to `ModernTools.slnx`, then create one or more `Base.Pages.PageBase` subclasses decorated with `[PageInfo]`. The shell discovers them by reflection — no registration needed. Keep everything feature-specific in this folder, and replace this file with a short description of what the feature does. See the root and `Base/CLAUDE.md` for framework rules.
