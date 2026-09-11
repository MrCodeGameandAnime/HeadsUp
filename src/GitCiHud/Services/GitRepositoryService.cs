using GitCiHud.Models;
using System.Globalization;

namespace GitCiHud.Services;

public interface IGitRepositoryService
{
    Task<bool> IsRepositoryAsync(string path, CancellationToken ct);
    Task<IReadOnlyList<string>> GetBranchesAsync(string path, CancellationToken ct);
    Task<RepositoryState> GetStateAsync(string path, string? branch, CancellationToken ct);
    Task<string?> GetGitHubSlugAsync(string path, CancellationToken ct);
}

public sealed class GitRepositoryService : IGitRepositoryService
{
    public async Task<bool> IsRepositoryAsync(string path, CancellationToken ct)
        => (await Git(path, "rev-parse --is-inside-work-tree", ct)).Output == "true";

    public async Task<IReadOnlyList<string>> GetBranchesAsync(string path, CancellationToken ct)
    {
        var result = await Git(path, "for-each-ref --format=\"%(refname:short)\" refs/heads refs/remotes", ct);
        return result.ExitCode == 0 ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(x => !x.EndsWith("/HEAD")).Distinct().Order().ToArray() : [];
    }

    public async Task<RepositoryState> GetStateAsync(string path, string? requestedBranch, CancellationToken ct)
    {
        if (!await IsRepositoryAsync(path, ct)) return new(Path.GetFileName(path), path, requestedBranch ?? "", null, null, false, SyncState.Unavailable, null, "Not a Git repository");
        var current = await Git(path, "branch --show-current", ct);
        var branch = string.IsNullOrWhiteSpace(requestedBranch) ? current.Output : requestedBranch!;
        var local = await Git(path, $"rev-parse {QuoteRef(branch)}", ct);
        if (local.ExitCode != 0) return new(Path.GetFileName(path), path, branch, null, null, false, SyncState.Unavailable, null, "Branch no longer exists");
        var remoteRef = branch.StartsWith("origin/") ? branch : $"origin/{branch}";
        var remote = await Git(path, $"rev-parse {QuoteRef(remoteRef)}", ct);
        var dirty = branch == current.Output && !(await Git(path, "status --porcelain", ct)).Output.Equals("");
        var date = await Git(path, $"show -s --format=%cI {QuoteRef(branch)}", ct);
        DateTimeOffset.TryParse(date.Output, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var commitTime);
        var sync = dirty ? SyncState.Dirty : remote.ExitCode != 0 ? SyncState.Unavailable : local.Output == remote.Output ? SyncState.Pushed : await GetRelationship(path, local.Output, remote.Output, ct);
        return new(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), path, branch, local.Output, remote.ExitCode == 0 ? remote.Output : null, dirty, sync, commitTime == default ? null : commitTime, remote.ExitCode == 0 ? null : "Remote unavailable");
    }

    public async Task<string?> GetGitHubSlugAsync(string path, CancellationToken ct)
    {
        var remote = await Git(path, "remote get-url origin", ct);
        if (remote.ExitCode != 0) return null;
        var value = remote.Output.Replace(".git", "", StringComparison.OrdinalIgnoreCase);
        var marker = "github.com";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        return value[(index + marker.Length)..].TrimStart(':', '/');
    }

    private async Task<SyncState> GetRelationship(string path, string local, string remote, CancellationToken ct)
    {
        var counts = await Git(path, $"rev-list --left-right --count {local}...{remote}", ct);
        var pair = counts.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return pair.Length != 2 ? SyncState.Unavailable : pair[0] == "0" ? SyncState.Behind : pair[1] == "0" ? SyncState.Unpushed : SyncState.Diverged;
    }
    private static string QuoteRef(string reference) => '"' + reference.Replace("\"", "") + '"';
    private static Task<(int ExitCode, string Output, string Error)> Git(string path, string args, CancellationToken ct) => ProcessRunner.RunAsync("git", args, path, ct);
}
