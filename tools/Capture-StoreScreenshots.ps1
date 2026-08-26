<#
.SYNOPSIS
    Drives EpubLiteReader with the demo book and captures the Store listing
    screenshot set into packaging/store-screenshots/. Not part of the build --
    a content-generation helper, run manually and reviewed by eye.

.DESCRIPTION
    Deterministic by design: the app is launched with --statefile=<path> so it
    mirrors its real UI state (mode, theme, navigation idleness, search status,
    About/Support state) to JSON, and every capture waits on that state plus a
    short paint settle -- no blind coordinate clicks, no fixed-delay guessing.
    Buttons and menu items are invoked through UI Automation by their
    accessible names.

.PARAMETER ExePath
    Optional explicit path to EpubLiteReader.exe. Use this to capture from the
    published/packaged layout (recommended for release screenshots), e.g.
    packaging\out\layout-win-x64\EpubLiteReader.exe. Defaults to the framework
    -dependent Release build output.
#>
param(
    [string]$Configuration = "Release",
    [string]$ExePath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if ($ExePath) {
    $exe = Get-Item $ExePath
} else {
    $exe = Get-ChildItem "$root\src\EpubLiteReader\bin\$Configuration\net10.0-windows*\EpubLiteReader.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\win-(x64|arm64)\\' } | Select-Object -First 1
}
if (-not $exe) { throw "EpubLiteReader.exe not found -- build first or pass -ExePath." }

$demo = Join-Path $root "packaging\store-screenshots\source\EpubLiteReader-demo.epub"
if (-not (Test-Path $demo)) { throw "Demo epub not found: $demo" }
$outDir = Join-Path $root "packaging\store-screenshots"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$stateFile = Join-Path ([System.IO.Path]::GetTempPath()) ("elr-capture-state-" + [guid]::NewGuid().ToString("N") + ".json")

Add-Type -Namespace Native -Name Methods -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
"@
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$winW = 1600
$winH = 900

function Read-AppState([string]$suffix = "") {
    $path = $stateFile + $suffix
    if (-not (Test-Path $path)) { return $null }
    try { return Get-Content $path -Raw | ConvertFrom-Json } catch { return $null }
}

function Wait-AppState([scriptblock]$predicate, [string]$what, [int]$timeoutSec = 30, [string]$suffix = "") {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        $state = Read-AppState $suffix
        if ($state -and (& $predicate $state)) {
            Start-Sleep -Milliseconds 400   # paint settle only; readiness came from real state
            return
        }
        Start-Sleep -Milliseconds 120
    }
    $last = Read-AppState $suffix
    $dump = if ($last) { $last | ConvertTo-Json -Compress } else { "<no state file>" }
    throw "Timed out waiting for: $what -- last app state: $dump"
}

function Test-OwnProcessForeground([int]$expectedPid) {
    $fg = [Native.Methods]::GetForegroundWindow()
    $fgPid = 0
    [Native.Methods]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
    return ($fgPid -eq $expectedPid)
}

# Windows' foreground lock makes bare SetForegroundWindow a no-op while another
# app holds focus. Escalate: plain call -> Alt-key unlock -> a real click on the
# window's own title bar (position derived live from GetWindowRect).
function Set-AppForeground([IntPtr]$hwnd, [int]$expectedPid, [bool]$allowTitleClick = $true) {
    if (Test-OwnProcessForeground $expectedPid) { return }
    [Native.Methods]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 250
    if (Test-OwnProcessForeground $expectedPid) { return }

    [Native.Methods]::keybd_event(0x12, 0, 0, 0)      # Alt down releases the lock
    [Native.Methods]::SetForegroundWindow($hwnd) | Out-Null
    [Native.Methods]::keybd_event(0x12, 0, 2, 0)      # Alt up
    Start-Sleep -Milliseconds 250
    if (Test-OwnProcessForeground $expectedPid) { return }

    if ($allowTitleClick) {
        $rect = New-Object Native.Methods+RECT
        [Native.Methods]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
        $x = [int](($rect.Left + $rect.Right) / 2)
        $y = $rect.Top + 15
        [Native.Methods]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 150
        [Native.Methods]::mouse_event(0x0002, 0, 0, 0, 0)
        [Native.Methods]::mouse_event(0x0004, 0, 0, 0, 0)
        Start-Sleep -Milliseconds 300
    }
    if (-not (Test-OwnProcessForeground $expectedPid)) {
        throw "Could not bring EpubLiteReader (PID $expectedPid) to the foreground."
    }
}

function Assert-OwnProcessForeground([int]$expectedPid) {
    # CopyFromScreen grabs whatever is on top, so if focus went elsewhere we
    # would silently ship a screenshot of another app. Fail loudly instead.
    if (-not (Test-OwnProcessForeground $expectedPid)) {
        $fg = [Native.Methods]::GetForegroundWindow()
        $fgPid = 0
        [Native.Methods]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
        throw "Foreground window belongs to PID $fgPid, not EpubLiteReader ($expectedPid); aborting."
    }
}

# The visible window bounds: GetWindowRect includes the invisible DWM
# resize/shadow border, which would leak a strip of whatever sits behind the
# window into the capture. DWMWA_EXTENDED_FRAME_BOUNDS (9) excludes it.
function Get-VisibleWindowRect([IntPtr]$hwnd) {
    $rect = New-Object Native.Methods+RECT
    $size = [System.Runtime.InteropServices.Marshal]::SizeOf([type]([Native.Methods+RECT]))
    if ([Native.Methods]::DwmGetWindowAttribute($hwnd, 9, [ref]$rect, $size) -ne 0) {
        [Native.Methods]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    }
    return $rect
}

# Hover tooltips must never photobomb a shot: keep the pointer parked in the
# far screen corner except while a deliberate click needs it.
function Move-CursorAway {
    $w = [System.Windows.Forms.SystemInformation]::VirtualScreen
    [Native.Methods]::SetCursorPos($w.Right - 2, $w.Bottom - 2) | Out-Null
}

function Save-WindowImage([IntPtr]$hwnd, [int]$expectedPid, [string]$path, [int]$maxWidth = 0) {
    Assert-OwnProcessForeground $expectedPid
    Move-CursorAway
    Start-Sleep -Milliseconds 250   # let any open tooltip fade
    $rect = Get-VisibleWindowRect $hwnd
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $g.Dispose()

    # Fullscreen captures the monitor's native resolution; downscale it so the
    # listing carousel is a uniform 16:9 set (still well above the 1366x768 min).
    if ($maxWidth -gt 0 -and $w -gt $maxWidth) {
        $nh = [int]([math]::Round($h * ($maxWidth / $w)))
        $scaled = New-Object System.Drawing.Bitmap $maxWidth, $nh
        $sg = [System.Drawing.Graphics]::FromImage($scaled)
        $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $sg.DrawImage($bmp, 0, 0, $maxWidth, $nh)
        $sg.Dispose(); $bmp.Dispose()
        $bmp = $scaled
        $w = $maxWidth; $h = $nh
    }

    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Saved $path ($w x $h)"
}

function Get-UiaRoot([IntPtr]$hwnd) {
    return [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
}

# Keyboard focus parked on a toolbar button makes WPF pop that button's
# tooltip into the shot (tooltip-on-keyboard-focus). Keeping focus on the
# reader document avoids that, and every app shortcut still works because the
# injected script forwards them.
function Focus-ReaderContent($mainElement) {
    try {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Document)
        $doc = $mainElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($doc) { $doc.SetFocus(); Start-Sleep -Milliseconds 200 }
    } catch { }
}

function Invoke-ByName($scopeElement, [string]$name, [int]$timeoutSec = 10) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $el = $scopeElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el) {
            $pattern = $null
            if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
                $pattern.Invoke()
                return
            }
        }
        Start-Sleep -Milliseconds 150
    }
    throw "UIA element named '$name' not found or not invokable."
}

# Menu items and dialog buttons live in separate top-level windows (WPF
# popups, owned dialogs): enumerate our process's top-level windows and
# search inside each.
function Invoke-InProcessByName([int]$ownPid, [string]$name, [int]$timeoutSec = 10) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $pidCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ownPid)
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        $tops = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children, $pidCond)
        foreach ($top in $tops) {
            $el = $top.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
            if ($el) {
                $pattern = $null
                if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
                    $pattern.Invoke()
                    Start-Sleep -Milliseconds 250   # let the menu close before sending keys
                    return
                }
                if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) {
                    $pattern.Toggle()
                    Start-Sleep -Milliseconds 250
                    return
                }
            }
        }
        Start-Sleep -Milliseconds 150
    }
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")   # close any stuck menu before failing
    throw "Element named '$name' not found in the app's windows."
}

Write-Host "Launching $($exe.FullName)"
$proc = Start-Process -FilePath $exe.FullName -ArgumentList "`"$demo`" --statefile=`"$stateFile`"" -PassThru

try {
    $hwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 60 -and $hwnd -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        $hwnd = $proc.MainWindowHandle
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw "Timed out waiting for main window." }

    # Wait for the book itself, not just the window.
    Wait-AppState { param($s) $s.book -and $s.navIdle } "demo book loaded" 60

    # Pin on top, position, size, focus. The outer size is padded so the
    # VISIBLE window (what gets captured) is exactly $winW x $winH.
    [Native.Methods]::ShowWindow($hwnd, 9) | Out-Null   # SW_RESTORE
    [Native.Methods]::SetWindowPos($hwnd, [IntPtr](-1), 40, 30, $winW, $winH, 0x0040) | Out-Null
    Start-Sleep -Milliseconds 400
    $outer = New-Object Native.Methods+RECT
    [Native.Methods]::GetWindowRect($hwnd, [ref]$outer) | Out-Null
    $visible = Get-VisibleWindowRect $hwnd
    $dw = ($outer.Right - $outer.Left) - ($visible.Right - $visible.Left)
    $dh = ($outer.Bottom - $outer.Top) - ($visible.Bottom - $visible.Top)
    [Native.Methods]::SetWindowPos($hwnd, [IntPtr](-1), 40, 30, $winW + $dw, $winH + $dh, 0x0040) | Out-Null
    Start-Sleep -Milliseconds 300
    Set-AppForeground $hwnd $proc.Id
    Move-CursorAway
    $mainUia = Get-UiaRoot $hwnd
    Focus-ReaderContent $mainUia

    # Deterministic baseline regardless of persisted per-book settings:
    # default typography (Ctrl+0) and the Light theme via the Settings menu.
    [System.Windows.Forms.SendKeys]::SendWait("^0")
    Invoke-ByName $mainUia "Settings"
    Invoke-InProcessByName $proc.Id "Light"
    Wait-AppState { param($s) $s.theme -eq "Light" -and [math]::Abs($s.fontScale - 1) -lt 0.01 } "light theme + default type"
    Focus-ReaderContent $mainUia

    # 1) Facing mode + chapter sidebar
    [System.Windows.Forms.SendKeys]::SendWait("2")
    Wait-AppState { param($s) $s.mode -eq "Facing" -and $s.navIdle } "facing mode"
    [System.Windows.Forms.SendKeys]::SendWait("{RIGHT}")   # spine 0 pairs with a blank right pane
    Wait-AppState { param($s) $s.mode -eq "Facing" -and $s.navIdle -and $s.spine -ge 1 } "facing spread with both pages"
    [System.Windows.Forms.SendKeys]::SendWait("{F4}")
    Wait-AppState { param($s) $s.chapterPane } "chapter sidebar open"
    Save-WindowImage $hwnd $proc.Id (Join-Path $outDir "1-facing-chapters.png")
    [System.Windows.Forms.SendKeys]::SendWait("{F4}")
    Wait-AppState { param($s) -not $s.chapterPane } "chapter sidebar closed"

    # 2) Continuous scroll
    [System.Windows.Forms.SendKeys]::SendWait("3")
    Wait-AppState { param($s) $s.mode -eq "Continuous" -and $s.navIdle } "continuous mode"
    Start-Sleep -Milliseconds 800   # let visible chapter frames finish their on-demand load
    Save-WindowImage $hwnd $proc.Id (Join-Path $outDir "2-continuous-scroll.png")

    # 3) Search with a visibly highlighted match, still in Continuous mode --
    #    this exercises the frame-aware find and shows the match counter.
    Invoke-InProcessByName $proc.Id "Search"
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait("ridge")
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    Wait-AppState { param($s) $s.searchStatus -and $s.searchStatus.Length -gt 0 } "search results"
    Save-WindowImage $hwnd $proc.Id (Join-Path $outDir "3-search-highlight.png")
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")

    # 4) Distraction-free full screen: facing mode with enlarged type fills a
    #    wide screen with two columns of text.
    [System.Windows.Forms.SendKeys]::SendWait("2")
    Wait-AppState { param($s) $s.mode -eq "Facing" -and $s.navIdle } "facing for fullscreen"
    [System.Windows.Forms.SendKeys]::SendWait("{RIGHT}")
    Wait-AppState { param($s) $s.navIdle -and $s.spine -ge 1 } "fullscreen spread"
    for ($i = 0; $i -lt 4; $i++) { [System.Windows.Forms.SendKeys]::SendWait("^{ADD}") }
    Wait-AppState { param($s) $s.fontScale -gt 1.3 } "enlarged type"
    [System.Windows.Forms.SendKeys]::SendWait("{F11}")
    Wait-AppState { param($s) $s.fullscreen } "fullscreen"
    $fs = Get-VisibleWindowRect $hwnd
    if (($fs.Right - $fs.Left) -le $winW) { throw "F11 did not enter full screen; aborting." }
    Save-WindowImage $hwnd $proc.Id (Join-Path $outDir "4-fullscreen-reading.png") -maxWidth 1920
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    Wait-AppState { param($s) -not $s.fullscreen } "left fullscreen"
    [System.Windows.Forms.SendKeys]::SendWait("^0")
    Wait-AppState { param($s) [math]::Abs($s.fontScale - 1) -lt 0.01 } "default type restored"
    [System.Windows.Forms.SendKeys]::SendWait("1")
    Wait-AppState { param($s) $s.mode -eq "Single" -and $s.navIdle } "single mode"

    # 5) Dark reading theme via the Settings menu (no coordinate clicks).
    Invoke-ByName $mainUia "Settings"
    Invoke-InProcessByName $proc.Id "Dark"
    Wait-AppState { param($s) $s.theme -eq "Dark" } "dark theme"
    Focus-ReaderContent $mainUia
    Save-WindowImage $hwnd $proc.Id (Join-Path $outDir "5-theme-dark.png")

    # 6) About window (captured over the main window, centered on it).
    Invoke-ByName $mainUia "Settings"
    Invoke-InProcessByName $proc.Id "About EPUB Lite Reader"
    Wait-AppState { param($s) $s.aboutOpen } "about window" 15 ".about"
    Save-WindowImage $hwnd $proc.Id (Join-Path $outDir "6-about.png")

    # 7) Contact support with the live form loaded (requires internet). No form
    #    fields are filled and nothing is submitted. The process-wide search
    #    reaches the About dialog's buttons the same way it reaches menu items.
    Invoke-InProcessByName $proc.Id "Contact support"
    Wait-AppState { param($s) $s.supportView } "support view" 15 ".about"
    Invoke-InProcessByName $proc.Id "Load support page"
    Wait-AppState { param($s) $s.supportLoaded } "support page loaded" 45 ".about"
    Start-Sleep -Milliseconds 1500   # remote page layout/fonts settle
    Save-WindowImage $hwnd $proc.Id (Join-Path $outDir "7-contact-support.png")
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
    Remove-Item -Force -ErrorAction SilentlyContinue $stateFile, "$stateFile.about"
}

Write-Host ""
Write-Host "== Verifying captured set =="
$failures = @()
foreach ($file in Get-ChildItem $outDir\*.png) {
    $img = [System.Drawing.Image]::FromFile($file.FullName)
    $ok = ($img.Width -ge 1366 -and $img.Height -ge 768 -and $file.Length -lt 50MB)
    $verdict = if ($ok) { "OK" } else { "FAIL: below Store minimum 1366x768 or over 50MB" }
    Write-Host ("{0}: {1}x{2} ({3:N0} bytes) {4}" -f $file.Name, $img.Width, $img.Height, $file.Length, $verdict)
    if (-not $ok) { $failures += $file.Name }
    $img.Dispose()
}
if ($failures) { throw "Screenshots violating Store requirements: $($failures -join ', ')" }
Write-Host "Done."
