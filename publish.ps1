<#
.SYNOPSIS
    Publishes ModernToolset as GitHub-style release zips: a self-contained "standalone"
    build (bundles the .NET runtime) and a "framework-dependent" build (needs the .NET 8
    Desktop Runtime installed). Each zip name includes the version.

.DESCRIPTION
    Both builds use the same layout: a single-file main executable plus the plugin DLLs
    under Plugins\<Name>\ (loaded at runtime). Output goes to dist\:
        ModernToolset_<version>_standalone.zip
        ModernToolset_<version>_framework-dependent.zip

.PARAMETER Version
    Override the version. Defaults to <InformationalVersion> from ModernTools.csproj.

.PARAMETER Mode
    Which builds to produce: 'both' (default), 'standalone', or 'framework'.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Mode standalone
    .\publish.ps1 -Version V01_00_19
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('both', 'standalone', 'framework')]
    [string]$Mode = 'both'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression           # ZipArchive / ZipArchiveMode
Add-Type -AssemblyName System.IO.Compression.FileSystem # ZipFile / ZipFileExtensions

$repo     = $PSScriptRoot
$mainProj = Join-Path $repo 'ModernTools\ModernTools.csproj'
$rid      = 'win-x64'
$config   = 'Release'
$appName  = 'ModernToolset'

# --- Resolve version from the csproj unless overridden ---
if (-not $Version) {
    $csprojText = Get-Content $mainProj -Raw
    if ($csprojText -match '<InformationalVersion>(.*?)</InformationalVersion>') {
        $Version = $Matches[1].Trim()
    }
    else {
        throw "Could not find <InformationalVersion> in $mainProj. Pass -Version explicitly."
    }
}
Write-Host "ModernToolset publish - version $Version" -ForegroundColor Cyan

# --- Plugin projects (Plugins subfolder name => project path) ---
# Mirrors the PublishPluginProjects target in ModernTools.csproj.
$plugins = [ordered]@{
    'Base'                 = 'Base\Base.csproj'
    'Audio'                = 'Features\Audio\Audio.csproj'
    'CommonProtocol'       = 'Features\CommonProtocol\CommonProtocol.csproj'
    'Gamepad'              = 'Features\Gamepad\GamePad.csproj'
    'GenericMouseAnalyzer' = 'Features\GenericMouseAnalyzer\GenericMouseAnalyzer.csproj'
    'KeyboardHallSensor'   = 'Features\KeyboardHallSensor\KeyboardHallSensor.csproj'
    'ArmouryProtocol'      = 'Features\ArmouryProtocol\ArmouryProtocol.csproj'
    'MouseATE'             = 'Features\MouseATE\MouseATE.csproj'
    'MCPServer'            = 'Features\MCPServer\MCPServer.csproj'
}

$distRoot  = Join-Path $repo 'dist'
$buildRoot = Join-Path $distRoot 'build'

function Publish-Bundle {
    param(
        [Parameter(Mandatory)][string]$ModeName,      # 'standalone' | 'framework-dependent'
        [Parameter(Mandatory)][bool]  $SelfContained
    )

    $folderName = "${appName}_${Version}_${ModeName}"
    $staging    = Join-Path $buildRoot $folderName
    $zipPath    = Join-Path $distRoot  "$folderName.zip"

    Write-Host "`n=== $ModeName ===" -ForegroundColor Green

    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    $scStr = if ($SelfContained) { 'true' } else { 'false' }
    $r2r   = if ($SelfContained) { 'true' } else { 'false' }

    # --- Main app (single-file). SkipPluginPublish=true: we publish plugins ourselves below.
    # Use -p:SelfContained (not --self-contained <val>) so the value is unambiguous: the CLI
    # flag form can mis-parse the separate 'false' token and bundle the runtime anyway. ---
    Write-Host "Publishing main app (self-contained=$scStr)..." -ForegroundColor Yellow
    & dotnet publish $mainProj -c $config -r $rid `
        -p:SelfContained=$scStr -p:PublishSingleFile=true -p:PublishReadyToRun=$r2r `
        -p:SkipPluginPublish=true -p:DebugType=none -p:DebugSymbols=false `
        -o $staging
    if ($LASTEXITCODE -ne 0) { throw "Main app publish failed ($ModeName)." }

    # --- Plugins: framework-dependent DLLs into Plugins\<Name>\ (identical for both modes). ---
    # Trailing forward slash on PublishDir avoids the PowerShell trailing-backslash quoting bug.
    foreach ($name in $plugins.Keys) {
        $proj      = Join-Path $repo $plugins[$name]
        $pluginDir = Join-Path $staging "Plugins\$name"
        Write-Host "Publishing plugin: $name" -ForegroundColor Yellow
        & dotnet msbuild $proj /t:Publish /p:PublishProfile=FolderProfile "/p:PublishDir=$pluginDir/"
        if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed: $name" }
    }

    # --- Dedupe: drop assemblies the host (main exe) already provides. The host loads its
    # own copies at startup, so plugin-folder duplicates are never used (LoadDLLsInFolder
    # skips already-loaded assemblies). Plugin-unique deps are not in $hostFiles, so kept. ---
    $removed = 0; $freed = 0L
    Get-ChildItem "$staging\Plugins" -Recurse -File -Filter *.dll |
        Where-Object { $hostFiles.ContainsKey($_.Name) } |
        ForEach-Object { $freed += $_.Length; $removed++; Remove-Item $_.FullName -Force }
    # Remove plugin directories left empty after dedupe (deepest first).
    Get-ChildItem "$staging\Plugins" -Directory -Recurse |
        Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem $_.FullName -Recurse -File) } |
        Remove-Item -Recurse -Force
    Write-Host ("Deduped {0} host-provided assemblies from plugins ({1:N1} MB)." -f $removed, ($freed / 1MB)) -ForegroundColor DarkGray

    # --- Strip any leftover PDBs ---
    Get-ChildItem $staging -Recurse -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

    # --- Zip with the named folder as the single top-level entry.
    # Build entries manually with forward-slash separators (.NET Framework's
    # CreateFromDirectory emits backslashes, which some extractors mishandle). ---
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $base    = (Resolve-Path $staging).Path.TrimEnd('\')
    $archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $staging -Recurse -File | ForEach-Object {
            $rel       = $_.FullName.Substring($base.Length + 1) -replace '\\', '/'
            $entryName = "$folderName/$rel"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }

    $sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "Created $zipPath ($sizeMB MB)" -ForegroundColor Cyan
    return $zipPath
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

# --- Authoritative set of assemblies the host (main app) ships, used to dedupe plugin
# folders. Same closure regardless of self-contained, so compute it once. ---
Write-Host "Computing host assembly manifest..." -ForegroundColor Yellow
$manifestDir = Join-Path $buildRoot '_host_manifest'
if (Test-Path $manifestDir) { Remove-Item $manifestDir -Recurse -Force }
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
& dotnet publish $mainProj -c $config -r $rid `
    -p:SelfContained=false -p:PublishSingleFile=false -p:PublishReadyToRun=false `
    -p:SkipPluginPublish=true -p:DebugType=none -p:DebugSymbols=false `
    -o $manifestDir | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Host manifest publish failed." }
$hostFiles = @{}
Get-ChildItem $manifestDir -Recurse -File -Filter *.dll | ForEach-Object { $hostFiles[$_.Name] = $true }
Write-Host "Host provides $($hostFiles.Count) assemblies." -ForegroundColor DarkGray

$zips = @()
if ($Mode -in @('both', 'standalone')) { $zips += Publish-Bundle -ModeName 'standalone'           -SelfContained $true }
if ($Mode -in @('both', 'framework'))  { $zips += Publish-Bundle -ModeName 'framework-dependent'   -SelfContained $false }

Write-Host "`n=== Done ===" -ForegroundColor Green
$zips | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
