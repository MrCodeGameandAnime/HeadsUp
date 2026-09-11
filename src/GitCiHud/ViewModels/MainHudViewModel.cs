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
    private readonly CancellationTokenSource _lifetime = new();
    private RepositoryState? _repository;
    private CiState? _ci;
    private UiPreferences _preferences;
    private bool _expanded;
    private int _refreshing;

    public MainHudViewModel(IGitRepositoryService git, IGitHubActionsService github, SettingsService settings, UiPreferences preferences)
        => (_git, _github, _settings, _preferences, _expanded) = (git, github, settings, preferences, preferences.Expanded);

    public event PropertyChangedEventHandler? PropertyChanged;
    public string RepositoryName => _repository?.Name ?? "Select a GitHub repository";
    public string Branch => _repository?.Branch ?? _preferences.Branch ?? "No branch";
    public string LocalSha => Short(_repository?.LocalHeadSha);
    public string RemoteSha => Short(_repository?.RemoteHeadSha);
    public string CiSha => Short(_ci?.HeadSha);
    public string FullLocalSha => _repository?.LocalHeadSha ?? "Unavailable";
    public string FullRemoteSha => _repository?.RemoteHeadSha ?? "Unavailable";
    public string FullCiSha => _ci?.HeadSha ?? "Waiting for matching run";
    public string LocalStatus => _repository is null ? "? unavailable" : SyncText(_repository.Sync);
    public string CommitAge => _repository?.CommitTime is { } d ? FriendlyAge(DateTimeOffset.Now - d) : "";
    public string CiStatusText => _ci is null ? "waiting for run" : CiText(_ci.Status);
    public string Workflow => _ci?.WorkflowName is { } name ? $"{name} #{_ci.RunNumber}" : _ci?.Error ?? "Waiting for matching run";
    public string Runtime => _ci?.StartedAt is { } d ? FriendlyAge((_ci.CompletedAt ?? DateTimeOffset.Now) - d) : "";
    public string DetailCi => $"{CiStatusText} {Runtime}".Trim();
    public IReadOnlyList<CiJob> Jobs => _ci?.Jobs ?? [];
    public bool Expanded { get => _expanded; set { if (_expanded != value) { _expanded = value; _preferences.Expanded = value; OnChanged(); _ = SaveAsync(); } } }
    public bool CanOpenRun => !string.IsNullOrWhiteSpace(_ci?.Url);
    public bool CanOpenFailedLogs => _ci?.Status == CiStatus.Failure && !string.IsNullOrWhiteSpace(_ci.Url);
    public IBrush AccentBrush => Brush.Parse(_preferences.AccentColor);
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
                var previousSha = _repository?.RemoteHeadSha;
                var remote = await _github.GetBranchShaAsync(_preferences.GitHubRepository, _preferences.Branch, _lifetime.Token);
                _repository = new RepositoryState(_preferences.GitHubRepository.Split('/').Last(), "", _preferences.Branch, null, remote.Sha, false,
                    SyncState.Unavailable, null, remote.Error);
                var shaChanged = !string.Equals(previousSha, remote.Sha, StringComparison.OrdinalIgnoreCase);
                if (shaChanged) _ci = CiState.Waiting(remote.Sha);
                NotifyAll();
                if (forceCi || shaChanged) await RefreshCiAsync();
                return;
            }
            if (string.IsNullOrWhiteSpace(_preferences.RepositoryPath)) return;
            var previousLocalSha = _repository?.LocalHeadSha;
            _repository = await _git.GetStateAsync(_preferences.RepositoryPath, _preferences.Branch, _lifetime.Token);
            var localShaChanged = !string.Equals(previousLocalSha, _repository.LocalHeadSha, StringComparison.OrdinalIgnoreCase);
            if (localShaChanged) _ci = CiState.Waiting(_repository.LocalHeadSha);
            NotifyAll();
            if (forceCi || localShaChanged) await RefreshCiAsync();
        }
        catch (OperationCanceledException) { }
        finally { Volatile.Write(ref _refreshing, 0); }
    }
    private async Task RefreshCiAsync()
    {
        var sha = _repository?.LocalHeadSha ?? _repository?.RemoteHeadSha;
        if (string.IsNullOrWhiteSpace(sha) || _ci?.IsTerminal == true && _ci.HeadSha == sha) return;
        var slug = _preferences.GitHubRepository ?? await _git.GetGitHubSlugAsync(_repository!.Path, _lifetime.Token);
        _ci = slug is null ? CiState.Unavailable("GitHub remote unavailable") : await _github.FindForShaAsync(slug, sha, _lifetime.Token);
        if (!string.Equals(_ci.HeadSha, sha, StringComparison.OrdinalIgnoreCase) && _ci.Status != CiStatus.Unavailable) _ci = CiState.Waiting(sha);
        NotifyAll();
    }
    public async Task SetRepositoryAsync(string path)
    {
        if (!await _git.IsRepositoryAsync(path, _lifetime.Token)) return;
        _preferences.RepositoryPath = path; _preferences.Branch = null; _repository = null; _ci = null;
        await SaveAsync(); await RefreshAsync(true);
    }
    public async Task SetGitHubRepositoryAsync(string input)
    {
        if (!TryParseGitHubReference(input, out var repository, out var branch)) return;
        _preferences.GitHubRepository = repository;
        _preferences.RepositoryPath = null;
        _preferences.Branch = branch;
        _repository = null;
        _ci = null;
        await SaveAsync();
        await RefreshAsync(true);
    }
    public async Task SetBranchAsync(string branch) { _preferences.Branch = branch; _ci = null; await SaveAsync(); await RefreshAsync(true); }
    public Task<IReadOnlyList<string>> GetBranchesAsync() => !string.IsNullOrWhiteSpace(_preferences.GitHubRepository)
        ? _github.GetBranchesAsync(_preferences.GitHubRepository, _lifetime.Token)
        : string.IsNullOrWhiteSpace(_preferences.RepositoryPath) ? Task.FromResult<IReadOnlyList<string>>([]) : _git.GetBranchesAsync(_preferences.RepositoryPath, _lifetime.Token);
    public async Task SetAccentAsync(string color) { _preferences.AccentColor = color; await SaveAsync(); NotifyAll(); }
    public async Task SetOpacityAsync(double opacity) { _preferences.Opacity = Math.Clamp(opacity, .15, 1); await SaveAsync(); NotifyAll(); }
    public void OpenRun() { if (CanOpenRun) Process.Start(new ProcessStartInfo(_ci!.Url!) { UseShellExecute = true }); }
    public void OpenFailedLogs() => OpenRun();
    private async Task RunLocalSchedulerAsync(CancellationToken ct) { using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2)); while (await timer.WaitForNextTickAsync(ct)) await RefreshAsync(false); }
    private async Task RunCiSchedulerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_ci?.Status == CiStatus.Unavailable ? 60 : 8), ct);
            if (_ci is null || !_ci.IsTerminal) await RefreshCiAsync();
        }
    }
    private Task SaveAsync() => _settings.SaveAsync(_preferences);
    private void NotifyAll() { foreach (var name in new[] { nameof(RepositoryName), nameof(Branch), nameof(LocalSha), nameof(RemoteSha), nameof(CiSha), nameof(FullLocalSha), nameof(FullRemoteSha), nameof(FullCiSha), nameof(LocalStatus), nameof(CommitAge), nameof(CiStatusText), nameof(Workflow), nameof(Runtime), nameof(DetailCi), nameof(Jobs), nameof(CanOpenRun), nameof(CanOpenFailedLogs), nameof(AccentBrush), nameof(SurfaceOpacity), nameof(SurfaceBrush) }) OnChanged(name); }
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
