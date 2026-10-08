param([string]$Executable = '.artifacts/publish/OgcDemo.Wpf.exe', [string]$Report = '.artifacts/desktop-smoke.json')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$exe = (Resolve-Path $Executable).Path
$reportPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Report))
$parent = Split-Path $reportPath -Parent
New-Item -ItemType Directory -Force $parent | Out-Null
if (Test-Path $reportPath) { throw 'Choose a new report name; this prevents mistaking an old report for a new test.' }
$p = Start-Process -FilePath $exe -ArgumentList @('--smoke-test', ('"' + $reportPath + '"')) -PassThru
if (!$p.WaitForExit(45000)) { throw 'Smoke test exceeded 45 seconds. Inspect the demo window; no other process was stopped.' }
if (!(Test-Path $reportPath)) { throw 'The app exited without a smoke-test report. Check the desktop runtime and startup configuration.' }
$result = Get-Content $reportPath -Raw | ConvertFrom-Json
if (!$result.success) { throw ('Desktop smoke failed: ' + $result.error) }
$result
