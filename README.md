# Git CI HUD

A deliberately small Avalonia desktop overlay for matching a selected local Git branch to the GitHub Actions run for its exact commit SHA.

## Prerequisites

- .NET 8 SDK or later
- Git on `PATH`
- GitHub CLI (`gh`) on `PATH`, authenticated with `gh auth login` (optional; local Git information still works without it)

## Run

```powershell
dotnet restore
dotnet run --project src/GitCiHud/GitCiHud.csproj
```

Right-click the HUD to select a repository, branch, color, or background opacity. Double-click it to expand details.

The app only reads Git state. It never checks out, fetches, pushes, or otherwise mutates a repository.

## Exact-SHA rule

The UI separately retains local, remote, and CI SHAs. A CI run is shown only after GitHub confirms that its `headSha` exactly equals the selected local SHA. Whenever the selected local SHA changes, the previous CI snapshot is discarded before another request is made.
