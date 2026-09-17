# HeadsUp CI

HeadsUp CI is a compact Windows desktop app for monitoring GitHub Actions for
the exact commit currently selected in a repository or local clone.

It keeps local Git state, the selected GitHub branch, and the matching CI run
separate so the HUD never reports a successful run for the wrong commit.

## Install

### Microsoft Store (recommended)

Install the signed Store version of HeadsUp CI. It includes the .NET runtime
and is the normal install path for most users.

Store ID: `9NMLS5FT4ZRW`

### Portable Windows x64

Download the [portable Windows x64 ZIP](https://github.com/MrCodeGameandAnime/HeadsUp/releases/download/v1.0.0.0/HeadsUp-CI-v1.0.0.0-win-x64.zip), extract it, and run `HeadsUp.exe`.

The portable build is framework-dependent and requires the **.NET 10 Desktop
Runtime**. It is intended for direct GitHub use and testing.

## What it does

- Monitors a GitHub repository and branch through the authenticated GitHub CLI.
- Matches a workflow run only when its `headSha` exactly equals the selected SHA.
- Optionally links a local clone to show pushed, unpushed, behind, diverged, or dirty state.
- Shows workflow status, run metadata, duration, and individual job results.
- Opens the run or failed-job page in a browser, or copies failed-job names
  when multiple jobs fail.
- Copies the full SHA, run ID, run URL, or a complete CI evidence summary.
- Polls local Git and remote GitHub state independently.
- Remembers settings, window position, size, appearance, polling interval, and always-on-top preference.

HeadsUp is local-first. It has no telemetry, advertising, analytics, built-in
cloud service, or user account. It only reads Git state and GitHub data through
the user's installed tools; it never checks out, fetches, pushes, or otherwise
mutates a repository.

## Requirements

- Windows 10 version 2004 or later.
- GitHub CLI (`gh`) on `PATH`, authenticated with `gh auth login`, for GitHub
  repository and Actions monitoring.
- **Portable ZIP only:** .NET 10 Desktop Runtime.
- **Development only:** .NET 10 SDK or later.
- Git on `PATH` when linking a local clone.

GitHub monitoring is optional when using HeadsUp only to inspect a local clone.
When a local clone is used without a repository entered in Settings, HeadsUp
can discover its GitHub repository from the clone's `origin` remote.

## Run from source

From the repository root:

```powershell
dotnet restore .\src\GitCiHud\GitCiHud.csproj --configfile .\NuGet.Config
dotnet run --project .\src\GitCiHud\GitCiHud.csproj
```

Click the gear button to open Settings. Enter a GitHub repository URL, load and
select a branch, and optionally choose a local clone folder. A URL containing
`/tree/<branch>` may be used to preselect a branch.

Settings also control accent color, surface opacity, polling interval, Always
on top, and—when running the unpackaged build—Start with Windows. The packaged
Store build does not currently expose the registry-based startup option.

## How matching works

HeadsUp treats the commit SHA as the identity of the state being monitored:

1. GitHub branch monitoring resolves the branch's current remote SHA.
2. Local-clone monitoring resolves the selected local branch SHA.
3. The previous CI snapshot is discarded whenever that selected SHA changes.
4. GitHub Actions runs are accepted only when their `headSha` exactly matches
   the selected SHA.

This prevents a newer or unrelated workflow run from being displayed as the
result for the commit currently shown in the HUD.

## Windows packages

The CI workflow produces two separate Windows artifacts:

- `HeadsUp-win-x64`: a lightweight framework-dependent development/testing
  publish.
- `HeadsUp-msix`: a self-contained MSIX Store payload that includes the .NET
  runtime.

### Development publish

```powershell
dotnet restore .\src\GitCiHud\GitCiHud.csproj --configfile .\NuGet.Config -r win-x64
dotnet publish .\src\GitCiHud\GitCiHud.csproj `
  -c Release -r win-x64 --self-contained false --no-restore `
  --output .\artifacts\HeadsUp-win-x64
```

### Build the Store MSIX

Install the Windows SDK so `MakeAppx.exe` and `MakePri.exe` are available,
then run:

```powershell
dotnet restore .\src\GitCiHud\GitCiHud.csproj --configfile .\NuGet.Config -r win-x64
dotnet publish .\src\GitCiHud\GitCiHud.csproj `
  -c Release -r win-x64 --self-contained true --no-restore `
  -p:PublishSingleFile=false `
  --output .\artifacts\HeadsUp-store-win-x64
.\packaging\Build-MSIX.ps1 -PublishDirectory ".\artifacts\HeadsUp-store-win-x64" -OutputDirectory ".\artifacts"
```

The result is `artifacts\HeadsUp-1.0.0.0.msix`. The package is intentionally
unsigned for local builds; Microsoft signs Store packages during certification.
The package version is controlled by `packaging\package-version.txt` and must
remain a valid `Major.Minor.Build.0` value.

The checked-in Store identity is:

- Name: `404Builds.HeadsUpCI`
- Display name: `HeadsUp CI`
- Publisher: `404 Builds`
- Architecture: `x64`
- Minimum Windows version: `10.0.19041.0`

See [packaging/README.md](packaging/README.md) for local signing, package
identity, asset validation, and submission guidance.

## Validation and CI

GitHub Actions checks repository hygiene, builds the Release configuration,
publishes the Windows artifact, validates the self-contained Store payload, and
builds the MSIX package. The current checked-in Windows App Certification Kit
report records a passing result for the 1.0.0.0 package:

[WACK report](<docs/WACK/WACK Report 09.12.26-09.56.htm>)

Any new release should repeat installation, launch, settings persistence,
uninstall, and WACK validation on a clean Windows environment.

## Privacy

See the [HeadsUp CI Privacy Policy](https://mrcodegameandanime.github.io/HeadsUp/) for local settings,
GitHub CLI, browser, clipboard, and data-retention details.
