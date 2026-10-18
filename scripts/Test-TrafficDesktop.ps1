param([string]$Executable = '.artifacts/traffic-publish/TrafficDemo.Wpf.exe', [string]$Report = '.artifacts/traffic/desktop-smoke.json', [switch]$NoBasemap)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$exe = (Resolve-Path $Executable).Path
$path = [IO.Path]::GetFullPath($Report)
if (Test-Path $path) { throw 'Choose a new report name.' }
New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
$arguments = @('--report', ('"' + $path + '"'), '--seconds', '10', '--ships', '100', '--aircraft', '100', '--dense', '--smoke')
if ($NoBasemap) { $arguments += '--no-basemap' }
$p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
$null = $p.Handle # Retain the native handle so Windows PowerShell preserves ExitCode.
if (!$p.WaitForExit(180000)) { throw 'Traffic smoke timed out; inspect the test window. No other process was stopped.' }
if ($p.ExitCode -ne 0) { throw "Traffic process exited with code $($p.ExitCode). Inspect native error logs and the report." }
if (!(Test-Path $path)) { throw 'No smoke report; check the .NET Windows Desktop runtime.' }
$result = Get-Content $path -Raw | ConvertFrom-Json
if (!$result.success) { throw ('Traffic smoke failed: ' + $result.error) }
$result
