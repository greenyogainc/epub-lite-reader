<#
.SYNOPSIS
    Builds EpubLiteReader and packs it into an MSIX ready for Microsoft Store submission.

.EXAMPLE
    .\Build-Msix.ps1                  # x64 MSIX in packaging\out
    .\Build-Msix.ps1 -Rid win-arm64   # ARM64 MSIX
#>
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Rid = "win-x64",
    [string]$Configuration = "Release",
    [string]$SignThumbprint = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root "src\EpubLiteReader\EpubLiteReader.csproj"
$outDir = Join-Path $PSScriptRoot "out"
$stage = Join-Path $outDir "layout-$Rid"

# Stamp the exact source commit into InformationalVersion for provenance.
$revision = (& git -C $root rev-parse HEAD 2>$null)
if (-not $revision) { $revision = "unknown" }

Write-Host "== Publishing ($Configuration / $Rid, rev $revision) ==" -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
dotnet publish $proj -c $Configuration -r $Rid --self-contained true `
    -p:PublishSingleFile=false -p:SourceRevisionId=$revision -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "== Staging manifest and assets ==" -ForegroundColor Cyan
$manifestPath = Join-Path $stage "AppxManifest.xml"
Copy-Item (Join-Path $PSScriptRoot "Package.appxmanifest") $manifestPath -Force
Copy-Item (Join-Path $PSScriptRoot "Assets") $stage -Recurse -Force

Get-ChildItem $stage -Filter *.pdb -Recurse | Remove-Item -Force

$arch = switch ($Rid) {
    "win-x64"   { "x64" }
    "win-arm64" { "arm64" }
    default     { throw "Unsupported RID '$Rid'." }   # unreachable past ValidateSet; defensive
}

# XmlDocument.Load/Save honor the XML declaration's utf-8 encoding on every
# PowerShell version. The previous Get-Content text round-trip read the file
# with the shell's default encoding, which mojibake'd non-ASCII text (em dash
# became "a-circumflex euro" bytes) in the packed 1.0.3 manifest.
$xml = New-Object System.Xml.XmlDocument
$xml.Load($manifestPath)
$xml.Package.Identity.SetAttribute("ProcessorArchitecture", $arch)
$xml.Save($manifestPath)

$manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
$manifestText = [System.Text.Encoding]::UTF8.GetString($manifestBytes)
if (-not $manifestText.Contains([char]0x2014)) {
    throw "Staged manifest lost its em dash - the encoding round-trip is broken."
}
if ($manifestText.Contains([char]0x00E2)) {
    throw "Staged manifest contains mojibake bytes - the encoding round-trip is broken."
}

Write-Host "== Locating makeappx.exe ==" -ForegroundColor Cyan
$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) {
    throw "makeappx.exe not found. Install the Windows 10/11 SDK (winget install Microsoft.WindowsSDK.10.0.26100)."
}

$ver = ($xml.Package.Identity.Version -split '\.')[0..2] -join '.'
$msix = Join-Path $outDir "EpubLiteReader-$ver-$Rid.msix"
Write-Host "== Packing $msix ==" -ForegroundColor Cyan
& $makeappx.FullName pack /d $stage /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

if ($SignThumbprint) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
        Sort-Object FullName -Descending | Select-Object -First 1
    Write-Host "== Signing ==" -ForegroundColor Cyan
    & $signtool.FullName sign /fd SHA256 /sha1 $SignThumbprint $msix
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
}

Write-Host "Done: $msix" -ForegroundColor Green
