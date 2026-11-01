<#
.SYNOPSIS
Runs the test suites with coverage and reports the app's merged coverage.

.DESCRIPTION
Each test project records its own Cobertura report. App coverage is the union of those
reports: for each line of the EpubLiteReader assembly, keep the higher hit count. Test
projects and tools are not counted.

The window suite opens real reader windows, a file dialog and message boxes, so it needs
an interactive desktop, the WebView2 runtime and nobody using the keyboard while it runs.
Use -Headless to skip it.

.PARAMETER Headless
Run without the window suite. Most of MainWindow and AboutWindow is then uncovered.

.PARAMETER MinLine
Fail when merged line coverage is below this percentage.

.PARAMETER ReportOnly
Report on the existing results instead of running the tests.

.EXAMPLE
pwsh tools/Measure-Coverage.ps1 -MinLine 100
#>
[CmdletBinding()]
param(
    [switch]$Headless,
    [double]$MinLine = 0,
    [switch]$ReportOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $root 'TestResults\coverage'

function Invoke-Suite([string]$Project, [bool]$Windows) {
    if ($Windows) { $env:ELR_UI = '1' } else { Remove-Item Env:ELR_UI -ErrorAction SilentlyContinue }
    & dotnet test (Join-Path $root $Project) -c Release --collect:'Code Coverage;Format=Cobertura' --results-directory $results
    return $LASTEXITCODE
}

if (-not $ReportOnly) {
    if (Test-Path $results) { Remove-Item $results -Recurse -Force }
    # One after the other: the window suite must not share the machine with anything racing it.
    $failed = (Invoke-Suite 'tests\EpubLiteReader.Tests\EpubLiteReader.Tests.csproj' $false) -ne 0
    if (-not $Headless) {
        $failed = ((Invoke-Suite 'tests\EpubLiteReader.UiTests\EpubLiteReader.UiTests.csproj' $true) -ne 0) -or $failed
    }
    if ($failed) {
        Write-Error 'Tests failed; coverage is not meaningful until they pass.'
    }
}

$reports = @(Get-ChildItem $results -Recurse -Filter '*.cobertura.xml')
if ($reports.Count -eq 0) { Write-Error "No coverage reports under $results." }

# file -> line -> hits, and file -> line -> covered/total branches, across every class and report.
$lines = @{}
$branches = @{}
foreach ($report in $reports) {
    [xml]$xml = Get-Content -LiteralPath $report.FullName -Raw
    foreach ($package in $xml.coverage.packages.package) {
        if ($package.name -ne 'EpubLiteReader') { continue }
        foreach ($class in $package.classes.class) {
            $file = $class.filename
            if ($file -match '[\\/]obj[\\/]') { continue }
            if (-not $lines.ContainsKey($file)) {
                $lines[$file] = @{}
                $branches[$file] = @{}
            }
            foreach ($line in $class.lines.line) {
                $number = [int]$line.number
                $hits = [long]$line.hits
                if (-not $lines[$file].ContainsKey($number) -or $lines[$file][$number] -lt $hits) {
                    $lines[$file][$number] = $hits
                }
                $condition = $line.'condition-coverage'
                if ($condition -and $condition -match '\((\d+)/(\d+)\)') {
                    $covered = [int]$Matches[1]
                    $total = [int]$Matches[2]
                    if (-not $branches[$file].ContainsKey($number) -or $branches[$file][$number][0] -lt $covered) {
                        $branches[$file][$number] = @($covered, $total)
                    }
                }
            }
        }
    }
}

$lineCovered = 0; $lineTotal = 0; $branchCovered = 0; $branchTotal = 0
$rows = foreach ($file in ($lines.Keys | Sort-Object)) {
    $fileLines = $lines[$file]
    $covered = @($fileLines.Keys | Where-Object { $fileLines[$_] -gt 0 }).Count
    $fileBranchCovered = 0; $fileBranchTotal = 0
    foreach ($pair in $branches[$file].Values) {
        $fileBranchCovered += $pair[0]
        $fileBranchTotal += $pair[1]
    }
    $lineCovered += $covered; $lineTotal += $fileLines.Count
    $branchCovered += $fileBranchCovered; $branchTotal += $fileBranchTotal
    [pscustomobject]@{
        File     = Split-Path -Leaf $file
        Lines    = "$covered/$($fileLines.Count)"
        'Line %' = [math]::Round(100 * $covered / [math]::Max(1, $fileLines.Count), 1)
        Branches = "$fileBranchCovered/$fileBranchTotal"
        Uncovered = (($fileLines.Keys | Where-Object { $fileLines[$_] -eq 0 } | Sort-Object) -join ',')
    }
}
$rows | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Host

$linePercent = 100 * $lineCovered / [math]::Max(1, $lineTotal)
$branchPercent = 100 * $branchCovered / [math]::Max(1, $branchTotal)
Write-Host ('App line coverage:   {0}/{1} = {2:N2}%' -f $lineCovered, $lineTotal, $linePercent)
Write-Host ('App branch coverage: {0}/{1} = {2:N2}% (includes compiler-generated branches)' -f $branchCovered, $branchTotal, $branchPercent)

if ($linePercent -lt $MinLine) {
    Write-Error ('Line coverage {0:N2}% is below the required {1}%.' -f $linePercent, $MinLine)
}
