using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
    private void OnFailedLogsClick(object? sender, RoutedEventArgs e) => Vm.OpenFailedLogs();

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
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select a Git repository", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) await Vm.SetRepositoryAsync(path);
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
