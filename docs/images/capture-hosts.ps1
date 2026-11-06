<#
.SYNOPSIS
  Screenshots the two sample hosts as real windows: the WinForms form that hosts the playground between two TextBoxes (FieldWorks' shape) and the plain Avalonia desktop window.
.DESCRIPTION
  Builds and starts each host through run.ps1, waits for its window, moves it to a fixed place and size, copies those screen pixels (System.Drawing, no extra software), saves a PNG
  into docs/images and closes the host. It needs an unlocked interactive Windows desktop and no window covering the host's place on the screen.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File docs\images\capture-hosts.ps1
#>
param(
    [string]$Scenario = 'Latin and Arabic',
    [int]$Width = 1280,
    [int]$Height = 800
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$out = $PSScriptRoot

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
}
'@
[void][Win]::SetProcessDPIAware()

function Capture([string]$Target, [string]$File, [string[]]$HostArgs) {
    $before = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in 'Host48', 'Playground.Desktop' } | ForEach-Object Id)
    & (Join-Path $root 'run.ps1') $Target @HostArgs | Out-Null
    $p = $null
    for ($i = 0; $i -lt 120 -and -not $p; $i++) {
        Start-Sleep -Milliseconds 500
        $p = Get-Process | Where-Object { $_.ProcessName -in 'Host48', 'Playground.Desktop' -and $before -notcontains $_.Id -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    }
    if (-not $p) { throw "No window for $Target" }
    try {
        $h = $p.MainWindowHandle
        [void][Win]::ShowWindow($h, 9)
        [void][Win]::MoveWindow($h, 20, 20, $Width, $Height, $true)
        [void][Win]::SetWindowPos($h, [IntPtr]::new(-1), 20, 20, $Width, $Height, 0x0040)   # topmost, so nothing else is in the copied pixels
        [void][Win]::SetForegroundWindow($h)
        Start-Sleep -Seconds 3   # first layout, fonts and the caret
        $r = New-Object Win+RECT
        # The visible frame, without the invisible resize border the window rectangle includes.
        if ([Win]::DwmGetWindowAttribute($h, 9, [ref]$r, [Runtime.InteropServices.Marshal]::SizeOf($r)) -ne 0) { [void][Win]::GetWindowRect($h, [ref]$r) }
        $w = $r.Right - $r.Left; $hgt = $r.Bottom - $r.Top
        $bmp = New-Object System.Drawing.Bitmap $w, $hgt
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
        $g.Dispose()
        $bmp.Save((Join-Path $out $File), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "Saved $File ($w x $hgt) from $Target"
    }
    finally {
        [void]$p.CloseMainWindow()
        if (-not $p.WaitForExit(5000)) { $p.Kill() }
    }
}

Capture 'winforms48' 'host-winforms-fieldworks-shape.png' @('--scenario', $Scenario)
Capture 'desktop10' 'desktop-window.png' @('--scenario', $Scenario)

