<#
.SYNOPSIS
    Drives EpubLiteReader.exe with the demo book and captures Store listing
    screenshots into packaging/store-screenshots/. Not part of the build --
    a one-off content-generation helper, run manually and reviewed by eye.
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$exe = Get-ChildItem "$root\src\EpubLiteReader\bin\$Configuration\net10.0-windows*\EpubLiteReader.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\win-(x64|arm64)\\' } | Select-Object -First 1
if (-not $exe) { throw "EpubLiteReader.exe not found under bin\$Configuration -- build first." }
$demo = Join-Path $root "packaging\store-screenshots\source\EpubLiteReader-demo.epub"
if (-not (Test-Path $demo)) { throw "Demo epub not found: $demo" }
$outDir = Join-Path $root "packaging\store-screenshots"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Add-Type -Namespace Native -Name Methods -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
"@
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function Assert-Foreground([IntPtr]$hwnd) {
    # CopyFromScreen grabs whatever is on top of those coordinates, so if the
    # app lost focus we would silently save screenshots of another window --
    # which has happened. Fail loudly instead of shipping the wrong picture.
    if ([Native.Methods]::GetForegroundWindow() -ne $hwnd) {
        throw "EpubLiteReader is not the foreground window; aborting so no bogus screenshots are written."
    }
}

function Save-WindowImage([IntPtr]$hwnd, [string]$path) {
    Assert-Foreground $hwnd
    $rect = New-Object Native.Methods+RECT
    [Native.Methods]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "Saved $path ($w x $h)"
}

function Invoke-ClickAt([int]$x, [int]$y) {
    [Native.Methods]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 150
    [Native.Methods]::mouse_event(0x0002, 0, 0, 0, 0)
    [Native.Methods]::mouse_event(0x0004, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 200
}

Write-Host "Launching $($exe.FullName)"
$proc = Start-Process -FilePath $exe.FullName -ArgumentList "`"$demo`"" -PassThru

try {
    $hwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 40 -and $hwnd -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        $hwnd = $proc.MainWindowHandle
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw "Timed out waiting for main window." }

    Start-Sleep -Seconds 3   # WebView2 init + first chapter render
    $winX = 60
    $winY = 40
    # Pin the window on top before anything else. Windows' foreground lock makes
    # SetForegroundWindow a no-op while another app holds focus, and a title-bar
    # click only helps once the window is actually the thing under the cursor --
    # otherwise the click lands on whatever is covering it (an editor, a browser)
    # and hands foreground to that instead.
    [Native.Methods]::ShowWindow($hwnd, 9) | Out-Null   # SW_RESTORE
    # HWND_TOPMOST, SWP_SHOWWINDOW: also positions and sizes in one call.
    [Native.Methods]::SetWindowPos($hwnd, [IntPtr](-1), $winX, $winY, 1100, 850, 0x0040) | Out-Null
    Start-Sleep -Milliseconds 500
    [Native.Methods]::SetForegroundWindow($hwnd) | Out-Null
    Invoke-ClickAt ($winX + 440) ($winY + 15)
    Start-Sleep -Milliseconds 700
    Assert-Foreground $hwnd

    # 1) Facing mode + chapter sidebar
    [System.Windows.Forms.SendKeys]::SendWait("2")
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait("{F4}")
    Start-Sleep -Milliseconds 800
    Save-WindowImage $hwnd (Join-Path $outDir "1-facing-pages.png")

    # 2) Continuous scroll, sidebar closed
    [System.Windows.Forms.SendKeys]::SendWait("{F4}")
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait("3")
    Start-Sleep -Milliseconds 600
    Save-WindowImage $hwnd (Join-Path $outDir "2-continuous-scroll.png")

    # 3) Search with a live match
    [System.Windows.Forms.SendKeys]::SendWait("^f")
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait("ridge")
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    Start-Sleep -Milliseconds 1800
    Save-WindowImage $hwnd (Join-Path $outDir "3-search.png")
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    Start-Sleep -Milliseconds 300

    # 4) Distraction-free full screen. Continuous scroll with a larger type size:
    # a single short chapter leaves a full-screen grab ~80% empty, which makes a
    # poor listing image, whereas flowing chapters actually fill the page.
    # Facing mode fills a wide screen with two columns of text; continuous
    # leaves tall gaps between chapters and single leaves most of the screen
    # empty. Switching mode also reloads the document, clearing the find
    # highlight left over from the search step.
    [System.Windows.Forms.SendKeys]::SendWait("2")
    Start-Sleep -Milliseconds 800
    [System.Windows.Forms.SendKeys]::SendWait("{HOME}")
    Start-Sleep -Milliseconds 700
    # Spine 0 pairs with a blank right pane; step forward so both pages carry text.
    [System.Windows.Forms.SendKeys]::SendWait("{RIGHT}")
    Start-Sleep -Milliseconds 900
    for ($i = 0; $i -lt 3; $i++) {
        [System.Windows.Forms.SendKeys]::SendWait("^{ADD}")
        Start-Sleep -Milliseconds 300
    }
    [System.Windows.Forms.SendKeys]::SendWait("{F11}")
    Start-Sleep -Milliseconds 1200
    # Confirm F11 actually landed: a "fullscreen" shot the size of the normal
    # window means the key never reached the app.
    $fs = New-Object Native.Methods+RECT
    [Native.Methods]::GetWindowRect($hwnd, [ref]$fs) | Out-Null
    if (($fs.Right - $fs.Left) -le 1100) { throw "F11 did not enter full screen; aborting." }
    Save-WindowImage $hwnd (Join-Path $outDir "4-fullscreen-reading.png")
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait("^0")     # back to default type size
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait("1")      # back to single-page mode
    Start-Sleep -Milliseconds 500

    # 5) Sepia reading theme (one Theme click from the default Light).
    Invoke-ClickAt ($winX + 848) ($winY + 48)
    Start-Sleep -Milliseconds 900
    Save-WindowImage $hwnd (Join-Path $outDir "5-theme-sepia.png")

    # 6) Dark reading theme (second Theme click).
    Invoke-ClickAt ($winX + 848) ($winY + 48)
    Start-Sleep -Milliseconds 900
    Save-WindowImage $hwnd (Join-Path $outDir "6-theme-dark.png")
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
}

Write-Host "Done."
