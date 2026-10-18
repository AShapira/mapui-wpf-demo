param([string]$Executable = '.artifacts/traffic-publish/TrafficDemo.Wpf.exe', [string]$WslDistribution = '', [switch]$SkipBasemapCheck)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (!$SkipBasemapCheck) {
    try { & "$PSScriptRoot/Ensure-TrafficBasemap.ps1" -WslDistribution $WslDistribution }
    catch { Write-Warning ($_.Exception.Message + ' Launching the viewer so simulation remains available.') }
}
$exe = (Resolve-Path $Executable).Path
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent)
