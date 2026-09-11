using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using GitCiHud.Models;
using GitCiHud.Services;

namespace GitCiHud.ViewModels;

public sealed class MainHudViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IGitRepositoryService _git;
    private readonly IGitHubActionsService _github;
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly CancellationTokenSource _lifetime = new();
    private RepositoryState? _repository;
    private CiState? _ci;
    private UiPreferences _preferences;
    private int _refreshing;
    private int _ciRefreshing;
    private readonly object _stateGate = new();
    private long _shaGeneration;

    public MainHudViewModel(IGitRepositoryService git, IGitHubActionsService github, SettingsService settings, UiPreferences preferences, StartupService? startup = null)
        => (_git, _github, _settings, _preferences, _startup) = (git, github, settings, preferences, startup ?? new StartupService());

    public event PropertyChangedEventHandler? PropertyChanged;
    public string RepositoryName => _preferences.GitHubRepository ?? _repository?.Name ?? "Select a GitHub repository";
    public string Branch => _repository?.Branch ?? _preferences.Branch ?? "No branch";
    public string RemoteSha => Short(_repository?.RemoteHeadSha);
    public string CiSha => Short(_ci?.HeadSha ?? _repository?.RemoteHeadSha);
    public string FullLocalSha => _repository?.LocalHeadSha ?? "Unavailable";
    public string FullRemoteSha => _repository?.RemoteHeadSha ?? "Unavailable";
    public string FullCiSha => _ci?.HeadSha ?? _repository?.RemoteHeadSha ?? "Waiting for matching run";
    public string LocalStatus => string.IsNullOrWhiteSpace(_preferences.RepositoryPath)
        ? "Not linked"
        : _repository is { } state && string.Equals(state.Path, _preferences.RepositoryPath, StringComparison.OrdinalIgnoreCase)
            ? SyncText(state.Sync)
            : "Linked";
    public string CommitAge => _repository?.CommitTime is { } d ? FriendlyAge(DateTimeOffset.Now - d) : "";
    public string CiStatusText => _ci is null ? "waiting for run" : CiText(_ci.Status);
    public string Workflow => _ci?.WorkflowName ?? _ci?.Error ?? "Waiting for matching run";
    public string RunNumberText => _ci?.RunNumber is { } number ? number.ToString() : "—";
    public string RunIdText => _ci?.RunId is { } id ? id.ToString() : "—";
    public string RunUrl => _ci?.Url ?? "—";
    public string Runtime => _ci?.StartedAt is { } d ? FriendlyAge((_ci.CompletedAt ?? DateTimeOffset.Now) - d) : "—";
    public string StartedText => _ci?.StartedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
    public string CompletedText => _ci?.CompletedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
    public string ConclusionText => _ci?.Status switch { CiStatus.Success => "success", CiStatus.Failure => "failure", CiStatus.Cancelled => "cancelled", CiStatus.Skipped => "skipped", _ => "—" };
    public string RunIdentity => _ci?.WorkflowName is { } name && _ci.RunNumber is { } number ? $"{name} #{number}" : "Waiting for matching run";
    public string DetailCi => $"{CiStatusText} {Runtime}".Trim();
    public IReadOnlyList<CiJob> Jobs => _ci?.Jobs ?? [];
    public IReadOnlyList<CiJob> FailedJobs => Jobs.Where(j => j.Status == CiStatus.Failure).ToArray();
    public string FailedJobsText => string.Join(Environment.NewLine, FailedJobs.Select(j => j.Name));
    public bool CanOpenRun => !string.IsNullOrWhiteSpace(_ci?.Url);
    public bool CanOpenFailedLogs => FailedJobs.Count > 0;
    public bool CanCopySha => !string.IsNullOrWhiteSpace(_ci?.HeadSha ?? _repository?.RemoteHeadSha);
    public bool CanCopyRunId => _ci?.RunId is not null;
    public bool CanCopyRunUrl => !string.IsNullOrWhiteSpace(_ci?.Url);
    public IBrush AccentBrush => Brush.Parse(_preferences.AccentColor);
    public IBrush StatusBrush => Brush.Parse(_ci?.Status switch
    {
        CiStatus.Success => "#8BE28B",
        CiStatus.Failure => "#FF8A8A",
        CiStatus.Running or CiStatus.Queued => "#FFD166",
        CiStatus.Cancelled or CiStatus.Skipped => "#B8C2CC",
        _ => _preferences.AccentColor
    });
    public IBrush SurfaceBrush
    {
        get
        {
            var color = Color.Parse(_preferences.AccentColor);
            return new SolidColorBrush(Color.FromArgb((byte)(_preferences.Opacity * 255), color.R, color.G, color.B));
        }
    }
    public double SurfaceOpacity => _preferences.Opacity;
    public UiPreferences Preferences => _preferences;
    public string EvidenceText => $"Repository: {RepositoryName}\nBranch: {Branch}\nSHA: {FullCiSha}\nWorkflow: {Workflow}\nRun Number: {RunNumberText}\nRun ID: {RunIdText}\nConclusion: {ConclusionText}\nRun: {RunUrl}";

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_preferences.GitHubRepository) || !string.IsNullOrWhiteSpace(_preferences.RepositoryPath)) await RefreshAsync(true);
        _ = RunLocalSchedulerAsync(_lifetime.Token);
        _ = RunCiSchedulerAsync(_lifetime.Token);
    }
    public async Task RefreshAsync(bool forceCi = true)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(_preferences.GitHubRepository))
            {
                if (string.IsNullOrWhiteSpace(_preferences.Branch)) return;
                var selectedRepository = _preferences.GitHubRepository;
                var selectedBranch = _preferences.Branch;
                long expectedGeneration;
                string? previousSha;
                lock (_stateGate)
                {
                    expectedGeneration = _shaGeneration;
                    previousSha = _repository?.RemoteHeadSha;
                }
                var remote = await _github.GetBranchShaAsync(selectedRepository, selectedBranch, _lifetime.Token);
                bool shaChanged;
                lock (_stateGate)
                {
                    if (expectedGeneration != _shaGeneration || !string.Equals(selectedRepository, _preferences.GitHubRepository, StringComparison.OrdinalIgnoreCase) || !string.Equals(selectedBranch, _preferences.Branch, StringComparison.Ordinal)) return;
                    _repository = new RepositoryState(selectedRepository.Split('/').Last(), "", selectedBranch, null, remote.Sha, false,
                        SyncState.Unavailable, null, remote.Error);
                    shaChanged = !string.Equals(previousSha, remote.Sha, StringComparison.OrdinalIgnoreCase);
                    if (shaChanged) { _shaGeneration++; _ci = CiState.Waiting(remote.Sha); }
                }
                NotifyAll();
                if (forceCi || shaChanged) await RefreshCiAsync();
                return;
            }
            if (string.IsNullOrWhiteSpace(_preferences.RepositoryPath)) return;
            var selectedPath = _preferences.RepositoryPath;
            var localSelectedBranch = _preferences.Branch;
            long localExpectedGeneration;
            string? previousLocalSha;
            lock (_stateGate)
            {
                localExpectedGeneration = _shaGeneration;
                previousLocalSha = _repository?.LocalHeadSha;
            }
            var repository = await _git.GetStateAsync(selectedPath, localSelectedBranch, _lifetime.Token);
            bool localShaChanged;
            lock (_stateGate)
            {
                if (localExpectedGeneration != _shaGeneration || !string.Equals(selectedPath, _preferences.RepositoryPath, StringComparison.OrdinalIgnoreCase) || !string.Equals(localSelectedBranch, _preferences.Branch, StringComparison.Ordinal)) return;
                _repository = repository;
                localShaChanged = !string.Equals(previousLocalSha, repository.LocalHeadSha, StringComparison.OrdinalIgnoreCase);
                if (localShaChanged) { _shaGeneration++; _ci = CiState.Waiting(repository.LocalHeadSha); }
            }
            NotifyAll();
            if (forceCi || localShaChanged) await RefreshCiAsync();
        }
        catch (OperationCanceledException) { }
        finally { Volatile.Write(ref _refreshing, 0); }
    }
    private async Task RefreshCiAsync()
    {
        // The local and CI schedulers can both notice the same state. Never
        // overlap gh lookups: besides wasting work, an older jobs request can
        // otherwise keep the process busy and temporarily inflate its footprint.
        if (Interlocked.Exchange(ref _ciRefreshing, 1) != 0) return;

        try
        {
        string? sha;
        string? slug;
        long expectedGeneration;
        CiState? currentCi;
        string? localPath;
        lock (_stateGate)
        {
            sha = _repository?.LocalHeadSha ?? _repository?.RemoteHeadSha;
            currentCi = _ci;
            expectedGeneration = _shaGeneration;
            localPath = _repository?.Path;
            slug = _preferences.GitHubRepository;
        }
        if (string.IsNullOrWhiteSpace(sha) || currentCi?.IsTerminal == true && currentCi.HeadSha == sha) return;
        if (slug is null && !string.IsNullOrWhiteSpace(localPath)) slug = await _git.GetGitHubSlugAsync(localPath, _lifetime.Token);
        var candidate = slug is null ? CiState.Unavailable("GitHub remote unavailable") : await _github.FindForShaAsync(slug, sha, _lifetime.Token);
        if (!string.Equals(candidate.HeadSha, sha, StringComparison.OrdinalIgnoreCase) && candidate.Status != CiStatus.Unavailable) candidate = CiState.Waiting(sha);
        lock (_stateGate)
        {
            var currentSha = _repository?.LocalHeadSha ?? _repository?.RemoteHeadSha;
            if (expectedGeneration != _shaGeneration || !string.Equals(currentSha, sha, StringComparison.OrdinalIgnoreCase)) return;
            _ci = candidate;
        }
        NotifyAll();
        }
        finally
        {
            Volatile.Write(ref _ciRefreshing, 0);
        }
    }
    public async Task SetRepositoryAsync(string path)
    {
        if (!await _git.IsRepositoryAsync(path, _lifetime.Token)) return;
        lock (_stateGate) { _preferences.RepositoryPath = path; if (string.IsNullOrWhiteSpace(_preferences.GitHubRepository)) _preferences.Branch = null; _repository = null; _ci = null; _shaGeneration++; }
        await SaveAsync(); await RefreshAsync(true);
    }
    public async Task SetGitHubRepositoryAsync(string input)
    {
        if (!TryParseGitHubReference(input, out var repository, out var branch)) return;
        lock (_stateGate)
        {
            _preferences.GitHubRepository = repository;
            _preferences.Branch = branch;
            _repository = null;
            _ci = null;
            _shaGeneration++;
        }
        await SaveAsync();
        await RefreshAsync(true);
    }
    public async Task SetBranchAsync(string branch) { lock (_stateGate) { _preferences.Branch = branch; _repository = null; _ci = null; _shaGeneration++; } await SaveAsync(); await RefreshAsync(true); }
    public Task<IReadOnlyList<string>> GetBranchesAsync() => !string.IsNullOrWhiteSpace(_preferences.GitHubRepository)
        ? _github.GetBranchesAsync(_preferences.GitHubRepository, _lifetime.Token)
        : string.IsNullOrWhiteSpace(_preferences.RepositoryPath) ? Task.FromResult<IReadOnlyList<string>>([]) : _git.GetBranchesAsync(_preferences.RepositoryPath, _lifetime.Token);
    public Task<IReadOnlyList<string>> GetBranchesForRepositoryAsync(string input)
        => TryParseGitHubReference(input, out var repository, out _)
            ? _github.GetBranchesAsync(repository, _lifetime.Token)
            : Task.FromResult<IReadOnlyList<string>>([]);
    public async Task SetAccentAsync(string color) { _preferences.AccentColor = color; await SaveAsync(); NotifyAll(); }
    public async Task SetOpacityAsync(double opacity) { _preferences.Opacity = Math.Clamp(opacity, .15, 1); await SaveAsync(); NotifyAll(); }
    public async Task SavePreferencesAsync() { _preferences.PollingSeconds = Math.Clamp(_preferences.PollingSeconds, 5, 60); await SaveAsync(); NotifyAll(); }
    public async Task SetStartWithWindowsAsync(bool enabled)
    {
        _startup.SetEnabled(enabled);
        _preferences.StartWithWindows = enabled;
        await SaveAsync();
        NotifyAll();
    }
    public async Task UnlinkLocalCloneAsync()
    {
        lock (_stateGate) { _preferences.RepositoryPath = null; _repository = null; _shaGeneration++; }
        await SaveAsync();
        if (!string.IsNullOrWhiteSpace(_preferences.GitHubRepository)) await RefreshAsync(true);
        else NotifyAll();
    }
    public void OpenRun() { if (CanOpenRun) Process.Start(new ProcessStartInfo(_ci!.Url!) { UseShellExecute = true }); }
    public void OpenFailedJob(CiJob job) { if (!string.IsNullOrWhiteSpace(job.Url)) Process.Start(new ProcessStartInfo(job.Url) { UseShellExecute = true }); }
    public void OpenFailedLogs() { if (FailedJobs.Count == 1) OpenFailedJob(FailedJobs[0]); else OpenRun(); }
    private async Task RunLocalSchedulerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Local filesystem state is cheap to sample frequently. A GitHub
            // branch head is remote state, so back it off while terminal CI is
            // displayed; the CI scheduler handles active-run updates separately.
            var hasLocalClone = !string.IsNullOrWhiteSpace(_preferences.RepositoryPath);
            var seconds = hasLocalClone
                ? 2
                : _ci?.IsTerminal == true
                    ? Math.Max(_preferences.PollingSeconds, 60)
                    : Math.Max(_preferences.PollingSeconds, 15);
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            await RefreshAsync(false);
        }
    }
    private async Task RunCiSchedulerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var seconds = _ci?.Status == CiStatus.Unavailable ? 60 : _preferences.PollingSeconds;
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            if (_ci is null || !_ci.IsTerminal) await RefreshCiAsync();
        }
    }
    private Task SaveAsync() => _settings.SaveAsync(_preferences);
    private void NotifyAll() { foreach (var name in new[] { nameof(RepositoryName), nameof(Branch), nameof(RemoteSha), nameof(CiSha), nameof(FullLocalSha), nameof(FullRemoteSha), nameof(FullCiSha), nameof(LocalStatus), nameof(CommitAge), nameof(CiStatusText), nameof(StatusBrush), nameof(Workflow), nameof(RunNumberText), nameof(RunIdText), nameof(RunUrl), nameof(Runtime), nameof(StartedText), nameof(CompletedText), nameof(ConclusionText), nameof(RunIdentity), nameof(DetailCi), nameof(Jobs), nameof(FailedJobs), nameof(FailedJobsText), nameof(CanOpenRun), nameof(CanOpenFailedLogs), nameof(CanCopySha), nameof(CanCopyRunId), nameof(CanCopyRunUrl), nameof(EvidenceText), nameof(AccentBrush), nameof(SurfaceOpacity), nameof(SurfaceBrush) }) OnChanged(name); }
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static string Short(string? sha) => string.IsNullOrEmpty(sha) ? "—" : sha[..Math.Min(12, sha.Length)];
    private static bool TryParseGitHubReference(string input, out string repository, out string? branch)
    {
        repository = ""; branch = null;
        var value = input.Trim().TrimEnd('/');
        if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) value = "https://github.com/" + value[15..];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        repository = $"{parts[0]}/{parts[1].Replace(".git", "", StringComparison.OrdinalIgnoreCase)}";
        if (parts.Length >= 4 && parts[2].Equals("tree", StringComparison.OrdinalIgnoreCase)) branch = Uri.UnescapeDataString(string.Join('/', parts[3..]));
        return true;
    }
    private static string SyncText(SyncState s) => s switch { SyncState.Pushed => "✓ pushed", SyncState.Unpushed => "↑ unpushed", SyncState.Behind => "↓ behind remote", SyncState.Diverged => "↕ diverged", SyncState.Dirty => "! dirty", _ => "? unavailable" };
    public static string CiText(CiStatus s) => s switch { CiStatus.Waiting => "waiting for matching run", CiStatus.Queued => "… queued", CiStatus.Running => "● running", CiStatus.Success => "✓ success", CiStatus.Failure => "✗ failure", CiStatus.Cancelled => "— cancelled", CiStatus.Skipped => "— skipped", CiStatus.Unavailable => "? unavailable", _ => "? unknown" };
    public static string JobSymbol(CiStatus s) => s switch { CiStatus.Success => "✓", CiStatus.Failure => "✗", CiStatus.Running => "⟳", CiStatus.Queued => "…", CiStatus.Skipped or CiStatus.Cancelled => "—", _ => "?" };
    private static string FriendlyAge(TimeSpan span) => span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds}s" : $"{Math.Max(0, (int)span.TotalSeconds)}s";
    public void Dispose() => _lifetime.Cancel();
}
