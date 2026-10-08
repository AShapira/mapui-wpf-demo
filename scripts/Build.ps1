param([string]$NuGetConfig = 'NuGet.local.Config', [switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (!(Test-Path $NuGetConfig)) { throw 'Copy NuGet.Config to NuGet.local.Config and replace the Artifactory placeholder.' }
if ((Get-Content $NuGetConfig -Raw).Contains('example.invalid')) { throw 'Configure a real internal Artifactory feed first.' }
function Run-DotNet { & dotnet @args | Out-Host; if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" } }
Run-DotNet restore OgcDemo.slnx --configfile $NuGetConfig --locked-mode
Run-DotNet build OgcDemo.slnx -c Release --no-restore
Run-DotNet test tests/OgcDemo.Tests -c Release --no-build --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory .artifacts/tests
Run-DotNet run --project tools/ManualBuilder -c Release --no-build --no-restore -- --check
if ($Publish) {
    Run-DotNet publish src/OgcDemo.Wpf -c Release -r win-x64 --self-contained false --no-restore -o .artifacts/publish
}
