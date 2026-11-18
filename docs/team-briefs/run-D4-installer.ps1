$ErrorActionPreference = 'Stop'
$repo = 'D:\Projects\skeer-audio\autostem'
$setup = Join-Path $repo 'launch\Skeer_autoSTEM_Setup_v1.1.0.exe'
$log = Join-Path $repo 'docs\team-briefs\D4-installer-upgrade-2026-11-18.log'

$args = @(
  '/VERYSILENT',
  '/SUPPRESSMSGBOXES',
  '/NORESTART',
  "/LOG=$log"
)

$proc = Start-Process -FilePath $setup -ArgumentList $args -Wait -PassThru
Write-Output ("SETUP_EXIT=" + $proc.ExitCode)
