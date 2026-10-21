$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

$out = New-Item -ItemType Directory -Force "smoke-output"
$logPath = Join-Path $env:LOCALAPPDATA "Ghostbar\ghostbar.log"
$summary = @()
$failed = 0

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Win {
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr h, out uint a);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static List<IntPtr> WindowsOf(uint pid, bool visibleOnly) {
    var list = new List<IntPtr>();
    EnumWindows((h, p) => {
      uint owner; GetWindowThreadProcessId(h, out owner);
      if (owner == pid && (!visibleOnly || IsWindowVisible(h))) list.Add(h);
      return true;
    }, IntPtr.Zero);
    return list;
  }
  public static void Chord() {
    keybd_event(0x11, 0, 0, UIntPtr.Zero);
    keybd_event(0x10, 0, 0, UIntPtr.Zero);
    keybd_event(0x20, 0, 0, UIntPtr.Zero);
    keybd_event(0x20, 0, 2, UIntPtr.Zero);
    keybd_event(0x10, 0, 2, UIntPtr.Zero);
    keybd_event(0x11, 0, 2, UIntPtr.Zero);
  }
}
"@

function Check([string]$name, [bool]$ok, [string]$detail = "") {
    $script:summary += "| $(if ($ok) { 'PASS' } else { 'FAIL' }) | $name | $detail |"
    Write-Host "$(if ($ok) { 'PASS' } else { 'FAIL' })  $name  $detail"
    if (-not $ok) { $script:failed++ }
}

function LogText { if (Test-Path $logPath) { Get-Content $logPath -Raw } else { "" } }

function WaitLog([string]$pattern, [int]$seconds = 20) {
    for ($i = 0; $i -lt $seconds * 2; $i++) {
        if ((LogText) -match $pattern) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

if (Test-Path $logPath) { Remove-Item $logPath -Force }

$proc = Start-Process "publish\Ghostbar.exe" -PassThru
Start-Sleep -Seconds 5
Check "Process stays running after start" (-not $proc.HasExited) "exit code: $(if ($proc.HasExited) { $proc.ExitCode } else { 'running' })"
Check "Start logged" (WaitLog "start v" 5)
Check "Toggle hotkey registered" (WaitLog "hotkey Ctrl\+Shift\+Space registered=True" 5) "log: hotkey line"

$visibleBefore = [Win]::WindowsOf([uint32]$proc.Id, $true)
Check "No visible window right after start" ($visibleBefore.Count -eq 0) "visible windows: $($visibleBefore.Count)"

[Win]::Chord()
Check "Hotkey reaches the app" (WaitLog "toggle hotkey pressed" 10) "keyboard injection + RegisterHotKey"
Check "WebView2 starts" (WaitLog "webview2 ready" 30) "runtime version in log"
Check "Chat page loads" (WaitLog "page loaded success=True" 30)
Check "JS bridge reaches C# (initModel)" (WaitLog "bridge initModel" 15)

Start-Sleep -Seconds 2
$visible = [Win]::WindowsOf([uint32]$proc.Id, $true)
Check "Chat window visible after hotkey" ($visible.Count -ge 1) "visible windows: $($visible.Count)"

if ($visible.Count -ge 1) {
    $h = $visible[0]
    $ex = [Win]::GetWindowLong($h, -20)
    Check "Hidden from taskbar/Alt+Tab (WS_EX_TOOLWINDOW)" (($ex -band 0x80) -ne 0) ("exstyle=0x{0:X}" -f $ex)
    Check "No WS_EX_APPWINDOW" (($ex -band 0x40000) -eq 0)
    $aff = 0
    [void][Win]::GetWindowDisplayAffinity($h, [ref]$aff)
    Check "Capture protection set (WDA_EXCLUDEFROMCAPTURE)" ($aff -eq 0x11) ("affinity=0x{0:X}" -f $aff)

    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ([System.Windows.Forms.SystemInformation]::VirtualScreen.Width), ([System.Windows.Forms.SystemInformation]::VirtualScreen.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([System.Windows.Forms.SystemInformation]::VirtualScreen.Location, [System.Drawing.Point]::Empty, $bmp.Size)
    $bmp.Save((Join-Path $out "screen-with-chat-open.png"))
    $cx = [int](($r.L + $r.R) / 2); $cy = [int](($r.T + $r.B) / 2)
    $px = $bmp.GetPixel($cx, $cy)
    $chatColor = ($px.R -eq 41 -and $px.G -eq 44 -and $px.B -eq 51)
    $summary += "| INFO | Screenshot capture check | pixel in window center = rgb($($px.R),$($px.G),$($px.B)); chat background is rgb(41,44,51), visible in capture: $chatColor |"
    Write-Host "INFO  window center pixel rgb($($px.R),$($px.G),$($px.B)), chat visible in capture: $chatColor"
}

[Win]::Chord()
Start-Sleep -Seconds 2
$visibleAfter = [Win]::WindowsOf([uint32]$proc.Id, $true)
Check "Second hotkey hides the window" ($visibleAfter.Count -eq 0) "visible windows: $($visibleAfter.Count)"

if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
Check "No errors in log" (-not ((LogText) -match "ERROR|FATAL")) "see ghostbar.log artifact"

if (Test-Path $logPath) { Copy-Item $logPath (Join-Path $out "ghostbar.log") }
$table = @("## Smoke test", "", "| Result | Check | Detail |", "|---|---|---|") + $summary
$table | Set-Content (Join-Path $out "summary.md")
if ($env:GITHUB_STEP_SUMMARY) { $table | Add-Content $env:GITHUB_STEP_SUMMARY }

if ($failed -gt 0) {
    Write-Host "---- ghostbar.log ----"
    LogText | Write-Host
    throw "$failed smoke checks failed"
}
