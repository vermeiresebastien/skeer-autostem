<#
.SYNOPSIS
    Captures the Skeer autoSTEM window to a PNG, optionally pressing a button first.

.DESCRIPTION
    Development helper for UI verification. Renders the app window with PrintWindow so the
    capture is clean even when other windows overlap it, then saves it to -Out.

.EXAMPLE
    ./capture-window.ps1 -Out docs/screenshots/02-ready.png
    ./capture-window.ps1 -Out docs/screenshots/03-details.png -InvokeButton "Row details"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$InvokeButton,
    [string]$WindowTitle = 'Skeer autoSTEM',
    [string]$Companion = 'artifacts/autostem-cli/autostem.exe'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;

public class SkeerCapture
{
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }

    public static IntPtr Find(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(512);
            GetWindowTextW(h, sb, 512);
            if (sb.ToString().Contains(title)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void Grab(IntPtr h, string path)
    {
        RECT r;
        GetWindowRect(h, out r);
        using (var bmp = new Bitmap(r.Rt - r.L, r.B - r.T))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(h, dc, 2);
                g.ReleaseHdc(dc);
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
"@ -ReferencedAssemblies System.Drawing

if ($InvokeButton) {
    & $Companion uia-invoke --button $InvokeButton --window $WindowTitle | Out-Null
    Start-Sleep -Seconds 2
}

$handle = [SkeerCapture]::Find($WindowTitle)
if ($handle -eq [IntPtr]::Zero) { throw "No visible window matching '$WindowTitle'." }

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

[SkeerCapture]::Grab($handle, (Join-Path (Get-Location) $Out))
Write-Host "saved $Out"
