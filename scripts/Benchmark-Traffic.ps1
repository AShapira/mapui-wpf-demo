param(
    [string]$Executable = '.artifacts/traffic-publish/TrafficDemo.Wpf.exe',
    [string]$OutputDirectory = ('.artifacts/traffic/benchmark-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [int]$Seconds = 60,
    [switch]$Soak,
    [switch]$FullMatrix
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$exe = (Resolve-Path $Executable).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $output) { throw 'Choose a new output directory; benchmark evidence is never overwritten.' }
New-Item -ItemType Directory $output | Out-Null
$cases = @()
foreach ($count in @(1000,5000,10000)) {
    foreach ($mix in @('ships','aircraft','mixed')) {
        foreach ($basemap in @($true,$false)) {
            $cases += [pscustomobject]@{ Count=$count; Mix=$mix; Basemap=$basemap; Dense=$true; Traces=$true; Seconds=$Seconds; Exercise=$true; Kind='matrix' }
        }
    }
}
if ($FullMatrix) {
    foreach ($count in @(1000,5000,10000)) {
        foreach ($mix in @('ships','aircraft','mixed')) {
            foreach ($dense in @($true,$false)) {
                foreach ($basemap in @($true,$false)) {
                    $cases += [pscustomobject]@{Count=$count; Mix=$mix; Basemap=$basemap; Dense=$dense; Traces=$false; Seconds=$Seconds; Exercise=$true; Kind='trace-comparison'}
                    if (!$dense) { $cases += [pscustomobject]@{Count=$count; Mix=$mix; Basemap=$basemap; Dense=$false; Traces=$true; Seconds=$Seconds; Exercise=$true; Kind='dispersed'} }
                }
            }
        }
    }
}
if ($Soak) { $cases += [pscustomobject]@{Count=10000; Mix='mixed'; Basemap=$true; Dense=$true; Traces=$true; Seconds=1800; Exercise=$false; Kind='soak'} }
$results = @()
foreach ($case in $cases) {
    $ships = if ($case.Mix -eq 'ships') {$case.Count} elseif ($case.Mix -eq 'aircraft') {0} else {$case.Count/2}
    $aircraft = $case.Count - $ships
    $name = '{0}-{1}-{2}-map{3}-dense{4}-traces{5}' -f $case.Kind,$case.Count,$case.Mix,[int]$case.Basemap,[int]$case.Dense,[int]$case.Traces
    $report = Join-Path $output ($name + '.json')
    $arguments = @('--report',('"'+$report+'"'),'--seconds',"$($case.Seconds)",'--ships',"$ships",'--aircraft',"$aircraft")
    if ($case.Dense) {$arguments += '--dense'}
    if (!$case.Basemap) {$arguments += '--no-basemap'}
    if (!$case.Traces) {$arguments += '--no-traces'}
    if ($case.Exercise) {$arguments += '--exercise'}
    Write-Output "Running $name ($($case.Seconds) seconds)"
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    $null = $p.Handle # Retain the native handle so Windows PowerShell preserves ExitCode.
    if (!$p.WaitForExit(($case.Seconds + 120)*1000)) { throw "Benchmark timed out: $name. Inspect that process before continuing." }
    if ($p.ExitCode -ne 0) { throw "Traffic process failed with exit code $($p.ExitCode): $name" }
    if (!(Test-Path $report)) { throw "No report from $name; check the runtime." }
    $result = Get-Content $report -Raw | ConvertFrom-Json
    $results += [pscustomobject]@{ Case=$name; FunctionalSuccess=$result.success; Acceptance=$result.acceptance; Report=$report }
    $results | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $output 'summary.json')
    # Continue recording other cases even when performance targets fail.
    if (!$result.success) { Write-Warning "Functional failure in $name; see report." }
}
$results
