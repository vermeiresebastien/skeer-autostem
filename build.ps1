<#
.SYNOPSIS
    Builds, tests and packages StemForge for Windows.

.DESCRIPTION
    Produces a single self-contained executable at artifacts\StemForge\StemForge.exe
    and a headless companion at artifacts\stemforge-cli\. Ableton Live remains a
    separately installed prerequisite.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
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

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Get-ChildItem $appOut -Filter *.exe | ForEach-Object { Write-Host ("  app       : {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
Get-ChildItem $cliOut -Filter *.exe | ForEach-Object { Write-Host ("  companion : {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
Write-Host ''
Write-Host 'Ableton Live 12.1+ is a separate prerequisite.' -ForegroundColor Yellow
