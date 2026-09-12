# HeadsUp MSIX packaging

The package manifest uses the Partner Center identity reserved for HeadsUp:

- Name: `404Builds.HeadsUpCI`
- Publisher: `CN=2BB531F8-77DD-4D82-AA5F-230F26EB09EE`
- Publisher display name: `404 Builds`

`package-version.txt` is the single initial package version (`0.1.0.0`). Every
Store submission must increase all or part of this four-part version; it is not
derived from a Git commit SHA.

## Build an unsigned Store package

Install the Windows 10/11 SDK so `MakeAppx.exe` is available, then run from the
repository root:

```powershell
dotnet restore --configfile .\NuGet.Config -r win-x64
dotnet publish .\src\GitCiHud\GitCiHud.csproj -c Release -r win-x64 --self-contained false --no-restore --output .\artifacts\HeadsUp-win-x64
packaging\Build-MSIX.ps1 -PublishDirectory .\artifacts\HeadsUp-win-x64 -OutputDirectory .\artifacts
```

The result is `artifacts\HeadsUp-0.1.0.0.msix`. It is intentionally unsigned:
Microsoft signs MSIX packages during Store certification, so no production
certificate or private key belongs in this repository.

For local sideload testing, pass a development `.pfx` to the packaging script:

```powershell
.\packaging\Build-MSIX.ps1 -PublishDirectory .\artifacts\HeadsUp-win-x64 `
  -OutputDirectory .\artifacts -CertificatePath .\local-headsup-test.pfx
```

The development certificate must be trusted on the test machine and its subject
must match the manifest publisher. Do not commit the certificate or password.

Before submitting a release candidate, run the Windows App Certification Kit
from an active Windows user session (the kit is installed with the Windows SDK):

```powershell
appcert.exe reset
appcert.exe test -appxpackagepath .\artifacts\HeadsUp-0.1.0.0.msix `
  -reportoutputpath .\artifacts\appcert
```

Review the generated HTML/XML report and separately test install, Start menu
launch, settings persistence, close/reopen, and uninstall on a clean test
environment.

The packaged build does not expose the unpackaged registry-based “Start with
Windows” setting. A future packaged startup implementation can add a manifest
startup task and wire it to the Windows startup-task API.
