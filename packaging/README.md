# HeadsUp MSIX packaging

The package manifest uses the Partner Center identity reserved for HeadsUp:

- Name: `404Builds.HeadsUpCI`
- Publisher: `CN=2BB531F8-77DD-4D82-AA5F-230F26EB09EE`
- Publisher display name: `404 Builds`

`package-version.txt` is the single initial package version (`1.0.0.0`). MSIX
Store versions use `Major.Minor.Build.Revision`: Major must be 1–65535, Minor
and Build must be 0–65535, and Revision must remain exactly `0`. Future Store
submissions should increment one of the first three components; the version is
not derived from a Git commit SHA.

## Build an unsigned Store package

Install the Windows 10/11 SDK so `MakePri.exe` and `MakeAppx.exe` are
available, then run these commands from the repository root (the directory
that contains `src`, `packaging`, and `NuGet.Config`). The Store payload must be
self-contained so it carries the .NET runtime and works on a clean compatible
Windows installation. `Build-MSIX.ps1` generates the required
`resources.pri` index for the qualified logo assets before packing:

```powershell
dotnet restore .\src\GitCiHud\GitCiHud.csproj --configfile .\NuGet.Config -r win-x64
dotnet publish .\src\GitCiHud\GitCiHud.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false --output .\artifacts\HeadsUp-store-win-x64
.\packaging\Build-MSIX.ps1 -PublishDirectory .\artifacts\HeadsUp-store-win-x64 -OutputDirectory .\artifacts
```

The result is `artifacts\HeadsUp-1.0.0.0.msix`. It is intentionally unsigned:
Microsoft signs MSIX packages during Store certification, so no production
certificate or private key belongs in this repository.

The separate `HeadsUp-win-x64` CI artifact remains framework-dependent for
lightweight development/testing. It is not the Store submission payload.

Package artwork is checked in under `packaging/Assets/` and is the source of
truth for the MSIX. `Build-MSIX.ps1` validates the supplied Windows asset
matrix and copies it into the package unchanged; it does not generate or
replace package artwork from `headsup-logo.png`.

The target-size and unplated taskbar variants use the `Square44x44Logo` stem
because that is the asset name referenced by `uap:VisualElements` in the
manifest (for example, `Square44x44Logo.targetsize-24_altform-unplated.png`).

For local sideload testing, pass a development `.pfx` to the packaging script:

```powershell
.\packaging\Build-MSIX.ps1 -PublishDirectory .\artifacts\HeadsUp-store-win-x64 `
  -OutputDirectory .\artifacts -CertificatePath .\local-headsup-test.pfx
```

The development certificate must be trusted on the test machine and its subject
must match the manifest publisher. Do not commit the certificate or password.

Before submitting a release candidate, run the Windows App Certification Kit
from an active Windows user session (the kit is installed with the Windows SDK):

```powershell
appcert.exe reset
appcert.exe test -appxpackagepath .\artifacts\HeadsUp-1.0.0.0.msix `
  -reportoutputpath .\artifacts\appcert
```

Review the generated HTML/XML report and separately test install, Start menu
launch, settings persistence, close/reopen, and uninstall on a clean test
environment.

The packaged build does not expose the unpackaged registry-based “Start with
Windows” setting. A future packaged startup implementation can add a manifest
startup task and wire it to the Windows startup-task API.

HeadsUp uses the authenticated GitHub CLI for repository and Actions data. New
users should install GitHub CLI (`gh`) and run `gh auth login`; the app reports
those setup steps when the CLI or authentication is missing.
