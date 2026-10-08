# 11 — Validate and publish a Windows desktop build

## Purpose and prerequisites

Produce a repeatable deliverable and state its validation limits accurately. Use a Windows workstation with the approved .NET 10 SDK and configured Artifactory feed. Keep work configuration separate from the distributable application.

## Step by step (Windows PowerShell)

1. Run the complete build script:

```powershell
.\scripts\Build.ps1 -Publish
```

It restores in locked mode, builds Release, runs tests, checks generated documentation, and publishes a framework-dependent Windows x64 application to `.artifacts/publish`.

2. Check the target workstation has the .NET 10 **Desktop Runtime**, not only the ASP.NET or base runtime. For an optional self-contained build, use the following; Artifactory must contain the relevant runtime packs, and the build may update runtime-specific restore assets:

```powershell
dotnet publish src/OgcDemo.Wpf -c Release -r win-x64 --self-contained true --configfile NuGet.local.Config -p:RestoreLockedMode=true -o .artifacts/publish-self-contained
```

3. Copy the entire publish folder to a user-writable test directory. Do not copy only the EXE; native SkiaSharp assets, assemblies, JSON, and samples are required. Trimming and single-file deployment are deliberately not enabled.
4. Place a reviewed `appsettings.local.json` beside the EXE on the destination only when needed. The project excludes private settings from publish output.
5. Run the opt-in Windows integration smoke check with a fresh report name:

```powershell
.\scripts\Test-Desktop.ps1 -Report .artifacts/desktop-smoke-new.json
```

The application creates its real window, exercises its in-process commands and rendered hit testing, checks the unavailable-service path, exports an image, writes a JSON report, and exits. This is not a physical mouse/keyboard walkthrough.
6. Perform the human acceptance pass: pan/wheel zoom, open a local GeoJSON file through the dialog, click shapes/holes, adjust tree controls, resize the window, connect your work services, and verify their real data and authentication. Record server/protocol versions and results without including credentials.

## Production deployment recommendations

Distribute through the organization's approved packaging/signing process. Treat the shipped version and dependency lock as a unit. Keep private configuration and credentials out of packages and source bundles. Choose a writable log directory and define retention explicitly; the demo's daily files do not delete old logs. Test display scaling, graphics drivers, enterprise proxy/TLS behavior, actual layer permissions, and payload sizes on representative workstations.

Windows Integrated Authentication, OAuth/SSO, credential-vault storage, signed installers, updates, and production telemetry are extension work. Do not claim the demo is a finished enterprise product.

## Checkpoint

The published application starts with its local layers, the smoke report records success, and the documentation check reports no stale outputs. Report core/fixture tests, Windows integration checks, manual desktop checks, and actual work-service checks as separate evidence. A successful HTTP response alone is not proof that a map is geographically correct.
