<#
.SYNOPSIS
    Builds, tests and packages StemForge for Windows.

.DESCRIPTION
    Produces a single self-contained executable at artifacts\StemForge\StemForge.exe,
    a headless companion at artifacts\stemforge-cli\, and an Inno Setup installer at
    artifacts\installers\. Whatever you actually run is then copied into launch\ at the
    repository root, so there is one folder to open the app from. Ableton Live remains a
    separately installed prerequisite.

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
        $compile = & $iscc (Join-Path $repo 'packaging/SkeerAutoSTEM.iss') 2>&1
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
Get-ChildItem $launch -Filter 'Skeer_autoSTEM_Setup_*.exe' | Remove-Item -Force

$setup = Get-ChildItem $installers -Filter 'Skeer_autoSTEM_Setup_*.exe' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($setup) { Copy-Item $setup.FullName $launch -Force }

Write-Host ''
Write-Host 'Done. Launch from:' -ForegroundColor Green
Get-ChildItem $launch -File | ForEach-Object { Write-Host ("  {0,-40} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
Write-Host ''
Write-Host 'Ableton Live 12.1+ is a separate prerequisite.' -ForegroundColor Yellow
if (-not $setup) { Write-Host 'No installer was produced this run.' -ForegroundColor Yellow }
