namespace GitCiHud.Models;

public enum SyncState { Unavailable, Pushed, Unpushed, Behind, Diverged, Dirty }
public enum CiStatus { Unavailable, Waiting, Queued, Running, Success, Failure, Cancelled, Skipped, Unknown }

public sealed record RepositoryState(string Name, string Path, string Branch, string? LocalHeadSha,
    string? RemoteHeadSha, bool Dirty, SyncState Sync, DateTimeOffset? CommitTime, string? Error);

public sealed record CiJob(string Name, CiStatus Status, string? Conclusion, string? Url);
public sealed record CiState(string? HeadSha, string? WorkflowName, int? RunNumber, long? RunId,
    string? Url, CiStatus Status, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    IReadOnlyList<CiJob> Jobs, string? Error)
{
    public static CiState Waiting(string? sha) => new(sha, null, null, null, null, CiStatus.Waiting, null, null, [], null);
    public static CiState Unavailable(string error) => new(null, null, null, null, null, CiStatus.Unavailable, null, null, [], error);
    public bool IsTerminal => Status is CiStatus.Success or CiStatus.Failure or CiStatus.Cancelled or CiStatus.Skipped;
}

public sealed class UiPreferences
{
    public string? RepositoryPath { get; set; }
    public string? Branch { get; set; }
    public double Left { get; set; } = 24;
    public double Top { get; set; } = 24;
    public double Width { get; set; } = 505;
    public double Opacity { get; set; } = .78;
    public string AccentColor { get; set; } = "#2D6A8E";
    public bool Expanded { get; set; }
}
