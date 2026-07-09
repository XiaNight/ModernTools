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
$appName   = 'ModernToolset'
$distRoot  = Join-Path $repo 'dist'
$stateFile = Join-Path $repo 'publish-versions.json'   # cache of last main / dev builds

# =====================================================================================
#  Version handling
#  Format: V(Major)_(HotFix)_(Minor)  e.g. V01_00_01
#  Significance (high -> low): Major > Minor > HotFix.
#    - HotFix++  : bump HotFix, keep Minor.
#    - Minor++   : bump Minor, reset HotFix.
#    - Major++   : bump Major, reset Minor + HotFix.
#  Major >= 90 (usually 99) denotes a dev build; main builds use Major < 90.
#  Last built versions are cached in publish-versions.json (no directory scanning).
# =====================================================================================

function ConvertFrom-ModernVersion {
    # Parse "V01_00_01" -> object, or $null if the string is not a valid version.
    param([string]$Text)
    if ($Text -and $Text.Trim() -match '^V(\d{2})_(\d{2})_(\d{2})$') {
        return [pscustomobject]@{
            Major  = [int]$Matches[1]
            HotFix = [int]$Matches[2]
            Minor  = [int]$Matches[3]
        }
    }
    return $null
}

function ConvertTo-ModernVersionString {
    param($V)
    return ('V{0:D2}_{1:D2}_{2:D2}' -f $V.Major, $V.HotFix, $V.Minor)
}

function Test-IsDevVersion {
    param($V)
    return ($V.Major -ge 90)
}

function Compare-ModernVersion {
    # Returns >0 if A>B, 0 if equal, <0 if A<B. Significance: Major > Minor > HotFix.
    param($A, $B)
    if ($A.Major -ne $B.Major) { return $A.Major  - $B.Major }
    if ($A.Minor -ne $B.Minor) { return $A.Minor  - $B.Minor }
    return $A.HotFix - $B.HotFix
}

function Get-VersionState {
    # Read the last main / dev build versions from the cache file.
    param([string]$Path)
    $state = [pscustomobject]@{ LastMain = $null; LastDev = $null }
    if (Test-Path $Path) {
        try {
            $j = Get-Content $Path -Raw | ConvertFrom-Json
            if ($j.lastMainBuild) { $state.LastMain = ConvertFrom-ModernVersion $j.lastMainBuild }
            if ($j.lastDevBuild)  { $state.LastDev  = ConvertFrom-ModernVersion $j.lastDevBuild }
        }
        catch { Write-Host "Warning: could not read $Path ($_); ignoring cache." -ForegroundColor Yellow }
    }
    return $state
}

function Save-VersionState {
    param([string]$Path, $State)
    $obj = [ordered]@{
        lastMainBuild = if ($State.LastMain) { ConvertTo-ModernVersionString $State.LastMain } else { $null }
        lastDevBuild  = if ($State.LastDev)  { ConvertTo-ModernVersionString $State.LastDev }  else { $null }
    }
    ($obj | ConvertTo-Json) | Set-Content -Path $Path
}

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

# --- Validate the version BEFORE building: it must parse and be strictly higher than
#     the last recorded build on the SAME track (main vs dev). If not, offer to
#     auto-increment (Y) or abort (n). ---
$parsed = ConvertFrom-ModernVersion $Version
$state  = Get-VersionState -Path $stateFile

$isDev     = if ($parsed) { Test-IsDevVersion $parsed } else { $false }
$lastSame  = if ($isDev) { $state.LastDev } else { $state.LastMain }
$trackName = if ($isDev) { 'dev' } else { 'main' }

$invalidReason = $null
if (-not $parsed) {
    $invalidReason = "'$Version' is not a valid version (expected V##_##_## e.g. V01_00_01)."
}
elseif ($lastSame -and (Compare-ModernVersion $parsed $lastSame) -le 0) {
    $invalidReason = ("'{0}' is not higher than the last {1} build '{2}'." -f `
        $Version, $trackName, (ConvertTo-ModernVersionString $lastSame))
}

if ($invalidReason) {
    $prevStr = if ($lastSame) { ConvertTo-ModernVersionString $lastSame } else { '(none)' }
    Write-Host ""
    Write-Host "Previous version: $prevStr" -ForegroundColor Yellow
    Write-Host "Current version:  $Version" -ForegroundColor Yellow
    Write-Host $invalidReason -ForegroundColor Red

    $ans = Read-Host "Auto-increment the version? (Y/n)"
    if ($ans -notmatch '^(y|yes|)$') {   # anything other than Y / Enter aborts
        throw "Aborted by user (version not incremented)."
    }

    # Increment relative to the higher of (current, last same-track build) so the
    # result is guaranteed to clear the last build on this track.
    $base = $parsed
    if ($lastSame -and (-not $base -or (Compare-ModernVersion $lastSame $base) -ge 0)) { $base = $lastSame }
    if (-not $base) { $base = [pscustomobject]@{ Major = 1; HotFix = 0; Minor = 0 } }

    $baseIsDev = Test-IsDevVersion $base

    # Reset rules: HotFix++ keeps Minor; Minor++ resets HotFix; Major++ resets both.
    $hotfixNew = [pscustomobject]@{ Major = $base.Major; HotFix = $base.HotFix + 1; Minor = $base.Minor }
    $minorNew  = [pscustomobject]@{ Major = $base.Major; HotFix = 0;                Minor = $base.Minor + 1 }

    # Major on a DEV build rolls onto the MAIN track: take the last main Major, +1, and
    # turn this into a main build. On a main build, just bump Major normally.
    if ($baseIsDev) {
        $mainMajor = if ($state.LastMain) { $state.LastMain.Major + 1 } else { 1 }
        $majorNew  = [pscustomobject]@{ Major = $mainMajor; HotFix = 0; Minor = 0 }
    }
    else {
        $majorNew  = [pscustomobject]@{ Major = $base.Major + 1; HotFix = 0; Minor = 0 }
    }
    $majorNote = if ($baseIsDev) { '  (dev -> main build)' } else { '' }

    $new = $null
    while (-not $new) {
        Write-Host "`nIncrement which part? (base $(ConvertTo-ModernVersionString $base))" -ForegroundColor Cyan
        Write-Host "  1. Hotfix: $(ConvertTo-ModernVersionString $hotfixNew)"
        Write-Host "  2. Minor:  $(ConvertTo-ModernVersionString $minorNew)"
        Write-Host "  3. Major:  $(ConvertTo-ModernVersionString $majorNew)$majorNote"
        Write-Host "  x. Abort"
        switch ((Read-Host "Choice").Trim().ToLower()) {
            '1' { $new = $hotfixNew }
            '2' { $new = $minorNew }
            '3' { $new = $majorNew }
            'x' { throw "Aborted by user (increment cancelled)." }
            default { Write-Host "Please enter 1, 2, 3, or x." -ForegroundColor Yellow }
        }
    }

    $Version = ConvertTo-ModernVersionString $new
    Write-Host "New version: $Version" -ForegroundColor Green

    # Persist the bump to the csproj so the compiled assembly matches the zip names.
    $csprojText = Get-Content $mainProj -Raw
    if ($csprojText -match '<InformationalVersion>(.*?)</InformationalVersion>') {
        $csprojText = $csprojText -replace '<InformationalVersion>.*?</InformationalVersion>', "<InformationalVersion>$Version</InformationalVersion>"
        Set-Content -Path $mainProj -Value $csprojText -NoNewline
        Write-Host "Updated <InformationalVersion> in $mainProj." -ForegroundColor DarkGray
    }
    else {
        Write-Host "Note: no <InformationalVersion> tag in csproj to update; using $Version for this build only." -ForegroundColor Yellow
    }

    $parsed    = $new
    $isDev     = Test-IsDevVersion $parsed
    $trackName = if ($isDev) { 'dev' } else { 'main' }
}

if ($isDev) {
    Write-Host "Note: Major $($parsed.Major) (>=90) - this is a DEV build." -ForegroundColor Magenta
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

# --- Record this build in the version cache so the next run can check quickly. ---
if ($isDev) { $state.LastDev = $parsed } else { $state.LastMain = $parsed }
Save-VersionState -Path $stateFile -State $state
Write-Host "Recorded $Version as last $trackName build in $stateFile." -ForegroundColor DarkGray

Write-Host "`n=== Done ===" -ForegroundColor Green
$zips | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
