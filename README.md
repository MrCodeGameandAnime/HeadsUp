# HeadsUp

A deliberately small Avalonia desktop overlay for matching a selected local Git branch to the GitHub Actions run for its exact commit SHA.

## Prerequisites

- .NET 10 SDK or later
- Git on `PATH`
- GitHub CLI (`gh`) on `PATH`, authenticated with `gh auth login` (optional; local Git information still works without it)

## Run

```powershell
dotnet restore
dotnet run --project src/GitCiHud/GitCiHud.csproj
```

Right-click the HUD to select a repository, branch, color, or background opacity. Double-click it to expand details.

The app only reads Git state. It never checks out, fetches, pushes, or otherwise mutates a repository.

## Windows packages

The normal `win-x64` publish is the development/testing artifact. The Store
candidate is built as an MSIX with the Windows SDK's `MakeAppx.exe`:

```powershell
dotnet restore --configfile .\NuGet.Config -r win-x64
dotnet publish .\src\GitCiHud\GitCiHud.csproj -c Release -r win-x64 --self-contained false --no-restore --output .\artifacts\HeadsUp-win-x64
.\packaging\Build-MSIX.ps1 -PublishDirectory .\artifacts\HeadsUp-win-x64 -OutputDirectory .\artifacts
```

See [packaging/README.md](packaging/README.md) for package identity, versioning,
and local signing guidance. CI uploads both `HeadsUp-win-x64` and
`HeadsUp-msix` separately.

## Exact-SHA rule

The UI separately retains local, remote, and CI SHAs. A CI run is shown only after GitHub confirms that its `headSha` exactly equals the selected local SHA. Whenever the selected local SHA changes, the previous CI snapshot is discarded before another request is made.
