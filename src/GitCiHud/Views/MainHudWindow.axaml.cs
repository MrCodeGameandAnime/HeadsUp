using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitCiHud.Models;
using GitCiHud.Services;
using GitCiHud.ViewModels;

namespace GitCiHud.Views;

public partial class MainHudWindow : Window
{
    private MainHudViewModel Vm => (MainHudViewModel)DataContext!;
    private readonly SettingsService _positionSettings = new();
    private CancellationTokenSource? _positionSave;

    public MainHudWindow()
    {
        InitializeComponent();
        PositionChanged += (_, _) => SavePosition();
        Closed += (_, _) => { _positionSave?.Cancel(); if (DataContext is MainHudViewModel vm) vm.Dispose(); };
    }

    public void ApplyPreferences(UiPreferences preferences)
    {
        Width = preferences.Width;
        Position = new PixelPoint((int)preferences.Left, (int)preferences.Top);
        if (preferences.Expanded) Height = 355;
    }

    private async void OnHudPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed) { await ShowMenuAsync(); e.Handled = true; return; }
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed && e.ClickCount == 2)
        {
            Vm.Expanded = !Vm.Expanded;
            Height = Vm.Expanded ? 355 : 255;
            e.Handled = true;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1) BeginMoveDrag(e);
    }
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await Vm.RefreshAsync();
    private void OnOpenRunClick(object? sender, RoutedEventArgs e) => Vm.OpenRun();
    private async void OnFailedLogsClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.FailedJobs.Count > 1) await CopyAsync(Vm.FailedJobsText);
        else Vm.OpenFailedLogs();
    }
    private async void OnCopyShaClick(object? sender, RoutedEventArgs e) => await CopyAsync(Vm.FullCiSha);
    private async void OnCopyRunIdClick(object? sender, RoutedEventArgs e) => await CopyAsync(Vm.RunIdText);
    private async void OnCopyRunUrlClick(object? sender, RoutedEventArgs e) => await CopyAsync(Vm.RunUrl);
    private async void OnCopyEvidenceClick(object? sender, RoutedEventArgs e) => await CopyAsync(Vm.EvidenceText);
    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    private async Task ShowMenuAsync()
    {
        var menu = new ContextMenu();
        var repository = new MenuItem { Header = "Repository…" };
        repository.Click += async (_, _) => await PickRepositoryAsync();
        menu.Items.Add(repository);

        var branches = new MenuItem { Header = "Branch" };
        foreach (var branch in await Vm.GetBranchesAsync())
        {
            var item = new MenuItem { Header = branch };
            item.Click += async (_, _) => await Vm.SetBranchAsync(branch);
            branches.Items.Add(item);
        }
        menu.Items.Add(branches);

        var colors = new MenuItem { Header = "Color" };
        foreach (var color in new[] { "#2D6A8E", "#285943", "#633B75", "#60472C", "#3D4654" })
        {
            var item = new MenuItem { Header = color };
            item.Click += async (_, _) => await Vm.SetAccentAsync(color);
            colors.Items.Add(item);
        }
        menu.Items.Add(colors);

        var opacity = new MenuItem { Header = "Transparency" };
        foreach (var choice in new[] { ("Very transparent", .25), ("Translucent", .55), ("Default", .78), ("Opaque", 1.0) })
        {
            var item = new MenuItem { Header = choice.Item1 };
            item.Click += async (_, _) => await Vm.SetOpacityAsync(choice.Item2);
            opacity.Items.Add(item);
        }
        menu.Items.Add(opacity);
        var refresh = new MenuItem { Header = "Refresh" }; refresh.Click += async (_, _) => await Vm.RefreshAsync(); menu.Items.Add(refresh);
        var close = new MenuItem { Header = "Close" }; close.Click += (_, _) => Close(); menu.Items.Add(close);
        menu.Open(this);
    }

    private async Task PickRepositoryAsync()
    {
        var input = new TextBox
        {
            Watermark = "https://github.com/owner/repository or …/tree/branch",
            Text = Vm.Preferences.GitHubRepository is { } repo ? $"https://github.com/{repo}" : "",
            MinWidth = 390
        };
        var hint = new TextBlock { Text = "Paste a GitHub repository URL. Include /tree/branch to select a branch now; otherwise choose Branch from the HUD menu.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = .75 };
        var save = new Button { Content = "Use repository", IsDefault = true, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 75 };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        panel.Children.Add(hint); panel.Children.Add(input); panel.Children.Add(buttons);
        var dialog = new Window { Title = "GitHub repository", Width = 510, Height = 175, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        save.Click += (_, _) => dialog.Close(input.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        var value = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(value)) await Vm.SetGitHubRepositoryAsync(value);
    }

    private void SavePosition()
    {
        if (DataContext is not MainHudViewModel vm) return;
        vm.Preferences.Left = Position.X;
        vm.Preferences.Top = Position.Y;
        vm.Preferences.Width = Width;
        _positionSave?.Cancel();
        _positionSave = new CancellationTokenSource();
        _ = SavePositionAfterMoveAsync(vm.Preferences, _positionSave.Token);
    }

    private async Task SavePositionAfterMoveAsync(UiPreferences preferences, CancellationToken cancellationToken)
    {
        try { await Task.Delay(400, cancellationToken); await _positionSettings.SaveAsync(preferences); }
        catch (OperationCanceledException) { }
    }
}
