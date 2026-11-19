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
    [string]$InvokeWindow,
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
        // Prefer an exact title. "Skeer autoSTEM" is a prefix of "Skeer autoSTEM - run finished", so a
        // substring search picks the dialog that popped over the main window instead of the window asked for.
        IntPtr exact = IntPtr.Zero;
        IntPtr contains = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(512);
            GetWindowTextW(h, sb, 512);
            var text = sb.ToString();
            if (string.Equals(text, title, StringComparison.OrdinalIgnoreCase)) { exact = h; return false; }
            if (contains == IntPtr.Zero && text.Contains(title)) contains = h;
            return true;
        }, IntPtr.Zero);
        return exact != IntPtr.Zero ? exact : contains;
    }

    public static string Title(IntPtr h)
    {
        var sb = new System.Text.StringBuilder(512);
        GetWindowTextW(h, sb, 512);
        return sb.ToString();
    }

    public static string Grab(IntPtr h, string path)
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
        return string.Format("'{0}' {1}x{2}", Title(h), r.Rt - r.L, r.B - r.T);
    }
}
"@ -ReferencedAssemblies System.Drawing

if ($InvokeButton) {
    # The button usually lives in the window we press from, which is not necessarily the window we
    # capture: pressing "Diagnostics" opens a second window, and -InvokeWindow names the first one.
    $pressWindow = if ($InvokeWindow) { $InvokeWindow } else { $WindowTitle }
    $pressed = & $Companion uia-invoke --button $InvokeButton --window $pressWindow
    if ($LASTEXITCODE -ne 0) {
        throw "uia-invoke '$InvokeButton' in '$pressWindow' exit=$LASTEXITCODE : $($pressed -join ' ')"
    }
    Write-Host $pressed
    Start-Sleep -Seconds 2
}

$handle = [SkeerCapture]::Find($WindowTitle)
if ($handle -eq [IntPtr]::Zero) { throw "No visible window matching '$WindowTitle'." }

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$what = [SkeerCapture]::Grab($handle, (Join-Path (Get-Location) $Out))
Write-Host "saved $Out  captured=$what"
