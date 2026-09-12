using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
        SizeChanged += (_, _) => SavePosition();
        Closed += (_, _) =>
        {
            _positionSave?.Cancel();
            if (DataContext is MainHudViewModel vm) vm.Dispose();
        };
    }

    public void ApplyPreferences(UiPreferences preferences)
    {
        Width = preferences.Width;
        Height = preferences.Height;
        Position = new PixelPoint((int)preferences.Left, (int)preferences.Top);
        Topmost = preferences.AlwaysOnTop;
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e) => await ShowSettingsAsync();
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
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && !string.IsNullOrWhiteSpace(text) && text != "—")
            await clipboard.SetTextAsync(text);
    }

    private async Task ShowSettingsAsync()
    {
        var preferences = Vm.Preferences;
        var repository = new TextBox
        {
            Text = preferences.GitHubRepository is { } repo ? $"https://github.com/{repo}" : "",
            Watermark = "https://github.com/owner/repository or …/tree/branch",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var branch = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        var loadBranches = new Button { Content = "Load branches", MinWidth = 105 };
        repository.TextChanged += (_, _) => branch.SelectedItem = null;
        var localClone = new TextBox
        {
            Text = preferences.RepositoryPath ?? "",
            Watermark = "Optional local clone folder",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var browse = new Button { Content = "Browse…", MinWidth = 78 };
        var unlink = new Button { Content = "Unlink", MinWidth = 68, IsEnabled = !string.IsNullOrWhiteSpace(preferences.RepositoryPath) };

        async Task LoadBranchesAsync()
        {
            try
            {
                var branches = !string.IsNullOrWhiteSpace(repository.Text)
                    ? await Vm.GetBranchesForRepositoryAsync(repository.Text)
                    : await Vm.GetBranchesAsync();
                branch.ItemsSource = branches;
                branch.SelectedItem = branches.FirstOrDefault(value => string.Equals(value, preferences.Branch, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                branch.ItemsSource = Array.Empty<string>();
            }
        }

        loadBranches.Click += async (_, _) => await LoadBranchesAsync();
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select optional local clone",
                AllowMultiple = false
            });
            if (folders.Count > 0 && folders[0].Path is { } path)
            {
                localClone.Text = path.LocalPath;
                unlink.IsEnabled = true;
            }
        };
        unlink.Click += (_, _) => { localClone.Text = ""; unlink.IsEnabled = false; };

        var accent = new ComboBox
        {
            ItemsSource = new[] { "#2D6A8E", "#285943", "#633B75", "#60472C", "#3D4654" },
            SelectedItem = preferences.AccentColor,
            MinWidth = 145
        };
        var transparency = new ComboBox { MinWidth = 145 };
        transparency.Items.Add(new ComboBoxItem { Content = "Very transparent", Tag = .25 });
        transparency.Items.Add(new ComboBoxItem { Content = "Translucent", Tag = .55 });
        transparency.Items.Add(new ComboBoxItem { Content = "Default", Tag = .78 });
        transparency.Items.Add(new ComboBoxItem { Content = "Opaque", Tag = 1.0 });
        transparency.SelectedIndex = preferences.Opacity switch { <= .4 => 0, <= .66 => 1, < .9 => 2, _ => 3 };
        var polling = new TextBox { Text = preferences.PollingSeconds.ToString(), Width = 70 };
        var isPackaged = Vm.IsPackaged;
        var startWithWindows = new CheckBox { Content = "Start with Windows", IsChecked = !isPackaged && preferences.StartWithWindows };
        var alwaysOnTop = new CheckBox { Content = "Always on top", IsChecked = preferences.AlwaysOnTop };

        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 82 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 82 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "GitHub repository", FontWeight = FontWeight.SemiBold });
        panel.Children.Add(SettingRow(repository, loadBranches));
        panel.Children.Add(new TextBlock { Text = "Branch", FontWeight = FontWeight.SemiBold });
        panel.Children.Add(branch);
        panel.Children.Add(new TextBlock { Text = "Local clone (optional)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(SettingRow(localClone, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { browse, unlink } }));
        panel.Children.Add(new TextBlock { Text = "Appearance", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(SettingRow(new TextBlock { Text = "Accent color", VerticalAlignment = VerticalAlignment.Center }, accent));
        panel.Children.Add(SettingRow(new TextBlock { Text = "Surface opacity", VerticalAlignment = VerticalAlignment.Center }, transparency));
        panel.Children.Add(SettingRow(new TextBlock { Text = "Polling interval (seconds)", VerticalAlignment = VerticalAlignment.Center }, polling));
        if (isPackaged)
            panel.Children.Add(new TextBlock
            {
                Text = "Start with Windows is unavailable in the Store build.",
                Opacity = .7,
                TextWrapping = TextWrapping.Wrap
            });
        else
            panel.Children.Add(startWithWindows);
        panel.Children.Add(alwaysOnTop);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "HeadsUp Settings",
            Width = 560,
            Height = 510,
            MinWidth = 500,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        dialog.Opened += async (_, _) => await LoadBranchesAsync();
        save.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        var accepted = await dialog.ShowDialog<bool?>(this);
        if (accepted != true) return;

        if (!string.IsNullOrWhiteSpace(repository.Text))
            await Vm.SetGitHubRepositoryAsync(repository.Text);
        if (branch.SelectedItem is string selectedBranch && !string.IsNullOrWhiteSpace(selectedBranch))
            await Vm.SetBranchAsync(selectedBranch);

        var clonePath = localClone.Text?.Trim();
        if (string.IsNullOrWhiteSpace(clonePath))
        {
            if (!string.IsNullOrWhiteSpace(preferences.RepositoryPath)) await Vm.UnlinkLocalCloneAsync();
        }
        else if (!string.Equals(clonePath, preferences.RepositoryPath, StringComparison.OrdinalIgnoreCase))
        {
            await Vm.SetRepositoryAsync(clonePath);
        }

        if (accent.SelectedItem is string selectedAccent) await Vm.SetAccentAsync(selectedAccent);
        if (transparency.SelectedItem is ComboBoxItem { Tag: double selectedOpacity }) await Vm.SetOpacityAsync(selectedOpacity);
        if (!int.TryParse(polling.Text, out var pollingSeconds)) pollingSeconds = preferences.PollingSeconds;
        preferences.PollingSeconds = Math.Clamp(pollingSeconds, 5, 60);
        preferences.AlwaysOnTop = alwaysOnTop.IsChecked == true;
        if (isPackaged)
            preferences.StartWithWindows = false;
        else
            await Vm.SetStartWithWindowsAsync(startWithWindows.IsChecked == true);
        await Vm.SavePreferencesAsync();
        Topmost = preferences.AlwaysOnTop;
    }

    private static Grid SettingRow(Control label, Control editor)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        row.Children.Add(label);
        Grid.SetColumn(label, 0);
        row.Children.Add(editor);
        Grid.SetColumn(editor, 1);
        editor.Margin = new Thickness(10, 0, 0, 0);
        return row;
    }

    private void SavePosition()
    {
        if (DataContext is not MainHudViewModel vm) return;
        vm.Preferences.Left = Position.X;
        vm.Preferences.Top = Position.Y;
        vm.Preferences.Width = Width;
        vm.Preferences.Height = Height;
        _positionSave?.Cancel();
        _positionSave = new CancellationTokenSource();
        _ = SavePositionAfterMoveAsync(vm.Preferences, _positionSave.Token);
    }

    private async Task SavePositionAfterMoveAsync(UiPreferences preferences, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await _positionSettings.SaveAsync(preferences);
        }
        catch (OperationCanceledException) { }
    }
}
