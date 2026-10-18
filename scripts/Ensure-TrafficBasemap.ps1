param(
    [string]$BaseUrl = 'http://127.0.0.1:8602/geoserver',
    [string]$ContainerName = 'east-europe-bundle',
    [string]$WslDistribution = '',
    [int]$TimeoutSeconds = 180,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
function Test-TrafficBasemap {
    try {
        $result = Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 -Uri ($BaseUrl.TrimEnd('/') + '/wms?service=WMS&request=GetCapabilities&version=1.3.0')
        $xml = New-Object System.Xml.XmlDocument
        $xml.XmlResolver = $null
        $xml.LoadXml($result.Content)
        return $xml.DocumentElement.LocalName -eq 'WMS_Capabilities' -and $result.Content.Contains('omt:dark-matter-en')
    } catch { return $false }
}
if (Test-TrafficBasemap) { Write-Output 'Existing OSM GeoServer is ready; reused without restart.'; exit 0 }
if ($CheckOnly) { throw 'OSM GeoServer is not ready.' }
$uri = [Uri]$BaseUrl
if (!$uri.IsLoopback -or $uri.Port -ne 8602) { throw 'Automatic start is restricted to the existing loopback port-8602 OSM deployment.' }
if ($WslDistribution) {
    # Only inspect/start the known existing container. No imports, volume recreation or machine changes.
    $name = if ($ContainerName -eq 'east-europe-bundle') { 'east-europe-bundle_geoserver_1' } else { $ContainerName }
    $ports = & wsl.exe --distribution $WslDistribution --exec podman inspect --format '{{json .HostConfig.PortBindings}}' $name 2>$null
    if ($LASTEXITCODE -ne 0 -or !(($ports | ConvertFrom-Json).'8080/tcp' | Where-Object HostPort -eq '8602')) { throw 'The selected WSL deployment does not expose the expected port. Verify the container and distribution.' }
    & wsl.exe --distribution $WslDistribution --exec podman start $name | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Starting the existing WSL GeoServer failed.' }
} else {
    $podman = Get-Command podman.exe -ErrorAction SilentlyContinue
    if (!$podman) { throw 'GeoServer is unavailable. Supply -WslDistribution for a WSL deployment, or start the prepared osm-basemap-demo bundle using its documented launcher.' }
    $ports = & $podman.Source inspect --format '{{json .HostConfig.PortBindings}}' $ContainerName 2>$null
    if ($LASTEXITCODE -ne 0 -or !(($ports | ConvertFrom-Json).'8080/tcp' | Where-Object HostPort -eq '8602')) { throw 'The selected Windows Podman container does not expose port 8602. For WSL, supply -WslDistribution and the existing container name.' }
    & $podman.Source start $ContainerName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Starting the existing GeoServer failed.' }
}
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    if (Test-TrafficBasemap) { Write-Output 'OSM GeoServer is ready.'; exit 0 }
    Start-Sleep -Seconds 2
} while ([DateTime]::UtcNow -lt $deadline)
throw 'GeoServer startup timed out. Inspect the OSM deployment logs; the viewer can still run without a basemap.'
