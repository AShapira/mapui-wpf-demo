# 02 — Restore, build, and launch

## Purpose and prerequisites

Turn the source into a running desktop application using only internal package infrastructure. Complete lesson 01 and obtain your organization's Artifactory NuGet v3 endpoint. The checked-in URL is intentionally unusable until configured.

## Step by step (Windows PowerShell)

1. Make the private feed configuration:

```powershell
Copy-Item NuGet.Config NuGet.local.Config
notepad NuGet.local.Config
```

Replace the `example.invalid` URL. Preserve `<clear />` so NuGet cannot inherit a public source. If authentication is required, use the organization's credential provider; see the configuration reference. Do not add credentials to the tracked template.

2. Restore with the checked-in lock files:

```powershell
dotnet restore OgcDemo.slnx --configfile NuGet.local.Config --locked-mode
```

The lock files pin transitive packages as well as direct references. A missing package in Artifactory is a feed preparation problem, not a reason to turn on public fallback. A content-hash mismatch should be investigated rather than bypassed.

3. Build and launch without triggering another restore:

```powershell
dotnet build OgcDemo.slnx --no-restore
dotnet run --project src/OgcDemo.Wpf --no-build --no-restore
```

4. Look for the blue application header, two local layers, synthetic geometries, and the feature-details panel. The absence of a public basemap is intentional: the neutral background proves that local content works independently of network services.
5. Run the tests after closing the app:

```powershell
dotnet test tests/OgcDemo.Tests --no-restore
```

## Recreating the skeleton in an empty directory

The equivalent starting commands below show how the solution is assembled. Run them only in a **new empty directory**, then replace the generated files with the complete listings from this guide; do not overwrite the supplied reference project with templates.

```powershell
dotnet new sln -n OgcDemo --format slnx
dotnet new classlib -n OgcDemo.Core -o src/OgcDemo.Core -f net10.0 --no-restore
dotnet new wpf -n OgcDemo.Wpf -o src/OgcDemo.Wpf -f net10.0 --no-restore
dotnet sln OgcDemo.slnx add src/OgcDemo.Core src/OgcDemo.Wpf
dotnet add src/OgcDemo.Wpf reference src/OgcDemo.Core
```

The complete source includes the remaining test/tool projects. The supplied project files, not unversioned `dotnet add package` commands, establish the dependency versions.

## Checkpoint

Build succeeds, the local demo opens, and no OGC endpoint is contacted until you explicitly connect a configured service. NU1701 warnings about SkiaSharp indicate an incorrect Windows target version; preserve the complete WPF project file. WPF cannot be launched with Linux `dotnet`, even when cross-compilation succeeds.
