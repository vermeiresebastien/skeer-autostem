<#
.SYNOPSIS
    Builds, tests and packages StemForge for Windows.

.DESCRIPTION
    Produces a self-contained desktop executable at artifacts\SkeerAutoSTEM\, a headless
    companion at artifacts\autostem-cli\, and an Inno Setup installer at
    artifacts\installers\. Both publish folders also receive the .NET runtime licence and
    third-party notices, because a self-contained build redistributes the runtime. Whatever
    you actually run is then copied into launch\ at the repository root, so there is one
    folder to open the app from. Ableton Live remains a separately installed prerequisite.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -SkipTests
    ./build.ps1 -SkipInstaller
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Write-Host 'Building solution...' -ForegroundColor Cyan
$solution = Get-ChildItem $repo -Filter 'StemForge.sln*' | Select-Object -First 1
if (-not $solution) { throw 'solution file not found' }
dotnet build $solution.FullName -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test (Join-Path $repo 'tests/StemForge.Tests/StemForge.Tests.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
}

$appOut = Join-Path $repo 'artifacts/SkeerAutoSTEM'
$cliOut = Join-Path $repo 'artifacts/autostem-cli'
Remove-Item $appOut, $cliOut -Recurse -Force -ErrorAction SilentlyContinue

Write-Host 'Publishing the desktop app (single file, self-contained)...' -ForegroundColor Cyan
dotnet publish (Join-Path $repo 'src/StemForge.App/StemForge.App.csproj') `
    -c Release -r win-x64 --self-contained true --nologo `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $appOut
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

Write-Host 'Publishing the headless companion...' -ForegroundColor Cyan
dotnet publish (Join-Path $repo 'tools/StemForge.Cli/StemForge.Cli.csproj') `
    -c Release -r win-x64 --self-contained true --nologo `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $cliOut
if ($LASTEXITCODE -ne 0) { throw 'companion publish failed' }

# Both executables above are self-contained, which means the .NET runtime ships inside them and
# Microsoft's licence and third-party notices have to ship beside them. They are copied out of the
# runtime packs the publish restored, so the notices match the runtime rather than whatever copy
# of the SDK happens to be on this machine.
function Get-NewestRuntimePack([string]$packageId) {
    $root = Join-Path $env:USERPROFILE ".nuget/packages/$packageId"
    if (-not (Test-Path $root)) { return $null }

    Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+(\.\d+){2,3}$' } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
}

$corePack = Get-NewestRuntimePack 'microsoft.netcore.app.runtime.win-x64'
$desktopPack = Get-NewestRuntimePack 'microsoft.windowsdesktop.app.runtime.win-x64'

if (-not $corePack -or -not $desktopPack) {
    throw 'the .NET runtime packs were not found in the NuGet cache; run a publish before packaging'
}
if ($corePack.Name -ne $desktopPack.Name) {
    throw "runtime packs disagree on version: core $($corePack.Name), desktop $($desktopPack.Name)"
}

$notices = @(
    @{ Source = (Join-Path $corePack.FullName 'THIRD-PARTY-NOTICES.TXT'); Name = 'ThirdPartyNotices.dotnet.txt' },
    @{ Source = (Join-Path $desktopPack.FullName 'LICENSE');              Name = 'LICENSE.dotnet-runtime.txt' }
)

foreach ($target in @($appOut, $cliOut)) {
    foreach ($notice in $notices) {
        if (-not (Test-Path $notice.Source)) { throw "notice file missing: $($notice.Source)" }
        Copy-Item $notice.Source (Join-Path $target $notice.Name) -Force
    }
}

Write-Host ("Shipping .NET {0} licence and third-party notices beside both executables" -f $corePack.Name) -ForegroundColor Cyan

$installers = Join-Path $repo 'artifacts/installers'

if (-not $SkipInstaller) {
    $iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        # INNO_SETUP_ISCC overrides discovery on machines where Inno lives off the
        # standard paths.
        $candidates = @(
            $env:INNO_SETUP_ISCC,
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            'D:\CURSOR\ISCC.exe'
        )
        $iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }

    if ($iscc) {
        Write-Host 'Compiling the installer...' -ForegroundColor Cyan
        # One version source: <Version> in the app's csproj. The define travels to ISCC here, and
        # the installer script refuses to default it, so a direct ISCC run must pass the same
        # /DAppVersion - a second literal here is the drift this file used to carry.
        $appProject = Join-Path $repo 'src/StemForge.App/StemForge.App.csproj'
        [xml]$appXml = Get-Content $appProject
        $version = @($appXml.Project.PropertyGroup | ForEach-Object { $_.Version }) |
            Where-Object { $_ } | Select-Object -First 1
        if (-not $version) { throw "no <Version> found in $appProject" }
        Write-Host ("installer version {0} (from {1})" -f $version, $appProject) -ForegroundColor Cyan
        $compile = & $iscc "/DAppVersion=$version" (Join-Path $repo 'packaging/SkeerAutoSTEM.iss') 2>&1
        if ($LASTEXITCODE -ne 0) { $compile | ForEach-Object { Write-Host $_ }; throw 'installer compile failed' }
    } else {
        Write-Warning 'Inno Setup (ISCC.exe) not found - building without an installer. Set INNO_SETUP_ISCC or pass -SkipInstaller.'
    }
}

# The unified launch folder: everything runnable, in one place, refreshed on every build.
# Only this script's own outputs are replaced, so shortcuts or notes you keep here survive.
$launch = Join-Path $repo 'launch'
New-Item -ItemType Directory -Path $launch -Force | Out-Null
Copy-Item (Join-Path $appOut 'SkeerAutoSTEM.exe') $launch -Force
Copy-Item (Join-Path $cliOut 'autostem.exe') $launch -Force
foreach ($notice in $notices) { Copy-Item (Join-Path $appOut $notice.Name) $launch -Force }
Copy-Item (Join-Path $repo 'LICENSE') $launch -Force
Get-ChildItem $launch -Filter 'Skeer_autoSTEM_Setup_*.exe' | Remove-Item -Force

$setup = Get-ChildItem $installers -Filter 'Skeer_autoSTEM_Setup_*.exe' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($setup) { Copy-Item $setup.FullName $launch -Force }

Write-Host ''
Write-Host 'Done. Launch from:' -ForegroundColor Green
Get-ChildItem $launch -File | ForEach-Object { Write-Host ("  {0,-40} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
Write-Host ''
Write-Host 'Ableton Live 12.3 Suite or newer is a separate prerequisite.' -ForegroundColor Yellow
if (-not $setup) { Write-Host 'No installer was produced this run.' -ForegroundColor Yellow }
