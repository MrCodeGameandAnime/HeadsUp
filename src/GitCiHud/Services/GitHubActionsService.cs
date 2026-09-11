using System.Text.Json;
using GitCiHud.Models;

namespace GitCiHud.Services;

public interface IGitHubActionsService
{
    Task<CiState> FindForShaAsync(string repository, string sha, CancellationToken ct);
    Task<IReadOnlyList<string>> GetBranchesAsync(string repository, CancellationToken ct);
    Task<(string? Sha, string? Error)> GetBranchShaAsync(string repository, string branch, CancellationToken ct);
}

public sealed class GitHubActionsService : IGitHubActionsService
{
    public async Task<IReadOnlyList<string>> GetBranchesAsync(string repository, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("gh", $"api repos/{repository}/branches --paginate --jq \".[].name\"", null, ct);
        return result.ExitCode == 0 ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order().ToArray() : [];
    }

    public async Task<(string? Sha, string? Error)> GetBranchShaAsync(string repository, string branch, CancellationToken ct)
    {
        var encodedBranch = Uri.EscapeDataString(branch);
        var result = await ProcessRunner.RunAsync("gh", $"api repos/{repository}/branches/{encodedBranch} --jq \".commit.sha\"", null, ct);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output)
            ? (result.Output, null)
            : (null, string.IsNullOrWhiteSpace(result.Error) ? "GitHub repository or branch unavailable" : CleanError(result.Error));
    }

    public async Task<CiState> FindForShaAsync(string repository, string sha, CancellationToken ct)
    {
        var list = await ProcessRunner.RunAsync("gh", $"run list --repo {repository} --commit {sha} --limit 20 --json databaseId,headSha,status,conclusion,name,number,url,startedAt,updatedAt", null, ct);
        if (list.ExitCode != 0) return CiState.Unavailable(string.IsNullOrWhiteSpace(list.Error) ? "GitHub CLI unavailable" : CleanError(list.Error));
        try
        {
            using var doc = JsonDocument.Parse(list.Output);
            var run = doc.RootElement.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("headSha", out var h) && string.Equals(h.GetString(), sha, StringComparison.OrdinalIgnoreCase));
            if (run.ValueKind == JsonValueKind.Undefined) return CiState.Waiting(sha);
            var id = run.GetProperty("databaseId").GetInt64();
            var view = await ProcessRunner.RunAsync("gh", $"run view {id} --repo {repository} --json databaseId,headSha,status,conclusion,name,number,url,startedAt,updatedAt,jobs", null, ct);
            if (view.ExitCode != 0) return ToState(run, sha, []);
            using var details = JsonDocument.Parse(view.Output);
            return ToState(details.RootElement, sha, ReadJobs(details.RootElement));
        }
        catch (JsonException) { return CiState.Unavailable("Unexpected GitHub CLI response"); }
    }

    private static CiState ToState(JsonElement run, string sha, IReadOnlyList<CiJob> jobs)
    {
        var stateSha = GetString(run, "headSha");
        if (!string.Equals(stateSha, sha, StringComparison.OrdinalIgnoreCase)) return CiState.Waiting(sha);
        var status = ToStatus(GetString(run, "status"), GetString(run, "conclusion"));
        return new(stateSha, GetString(run, "name"), GetInt(run, "number"), GetLong(run, "databaseId"), GetString(run, "url"), status,
            GetDate(run, "startedAt"), GetDate(run, "updatedAt"), jobs, null);
    }
    private static IReadOnlyList<CiJob> ReadJobs(JsonElement root)
    {
        if (!root.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array) return [];
        return jobs.EnumerateArray().Select(j => new CiJob(GetString(j, "name") ?? "Unnamed job", ToStatus(GetString(j, "status"), GetString(j, "conclusion")), GetString(j, "conclusion"), GetString(j, "url"))).ToArray();
    }
    private static CiStatus ToStatus(string? status, string? conclusion) => conclusion?.ToLowerInvariant() switch
    {
        "success" => CiStatus.Success, "failure" or "timed_out" or "action_required" => CiStatus.Failure,
        "cancelled" => CiStatus.Cancelled, "skipped" or "neutral" => CiStatus.Skipped, _ => status?.ToLowerInvariant() switch
        { "queued" or "pending" => CiStatus.Queued, "in_progress" or "running" => CiStatus.Running, "completed" => CiStatus.Unknown, _ => CiStatus.Unknown }
    };
    private static string? GetString(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;
    private static int? GetInt(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var x) ? x : null;
    private static long? GetLong(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var x) ? x : null;
    private static DateTimeOffset? GetDate(JsonElement e, string name) => DateTimeOffset.TryParse(GetString(e, name), out var date) ? date : null;
    private static string CleanError(string input) => input.Contains("not logged", StringComparison.OrdinalIgnoreCase) ? "GitHub authentication required" : "GitHub unavailable";
}
