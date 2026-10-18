#!/usr/bin/env bash
set -euo pipefail
# Reuse the existing small-gis-demos deployment; never provision or replace its volumes.
base_url="${TRAFFIC_BASEMAP_URL:-http://127.0.0.1:8602/geoserver}"
container="${TRAFFIC_BASEMAP_CONTAINER:-east-europe-bundle_geoserver_1}"
engine="${TRAFFIC_CONTAINER_ENGINE:-podman}"
ready() {
  curl --fail --silent --max-time 5 "$base_url/wms?service=WMS&request=GetCapabilities&version=1.3.0" |
    python3 -c 'import sys,xml.etree.ElementTree as E; r=E.parse(sys.stdin).getroot(); sys.exit(0 if r.tag.endswith("WMS_Capabilities") and any(e.text=="omt:dark-matter-en" for e in r.iter()) else 1)' 2>/dev/null
}
if ready; then echo 'Existing OSM GeoServer is ready; reused without restart.'; exit 0; fi
if [[ "${1:-}" == '--check-only' ]]; then echo 'OSM GeoServer is not ready.' >&2; exit 1; fi
if [[ "$base_url" != 'http://127.0.0.1:8602/geoserver' ]]; then echo 'Automatic start requires the known loopback port-8602 deployment.' >&2; exit 1; fi
ports="$("$engine" inspect --format '{{json .HostConfig.PortBindings}}' "$container")"
if ! python3 -c 'import json,sys; p=json.loads(sys.argv[1]); sys.exit(0 if any(str(x.get("HostPort"))=="8602" for x in p.get("8080/tcp",[]) or []) else 1)' "$ports"; then echo 'Existing container does not expose port 8602; verify deployment.' >&2; exit 1; fi
"$engine" start "$container" >/dev/null
for (( attempt=0; attempt<90; attempt++ )); do
  if ready; then echo 'OSM GeoServer is ready.'; exit 0; fi
  sleep 2
done
echo 'GeoServer startup timed out. Inspect deployment logs; overlay-only mode remains available.' >&2
exit 1
