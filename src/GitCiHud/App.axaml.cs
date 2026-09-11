using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitCiHud.Services;
using GitCiHud.ViewModels;
using GitCiHud.Views;

namespace GitCiHud;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsService();
            // Settings are a small local JSON file. Read them before the shell is shown,
            // but never make the window wait on Git or GitHub I/O.
            var preferences = settings.Load();
            var vm = new MainHudViewModel(new GitRepositoryService(), new GitHubActionsService(), settings, preferences);
            var window = new MainHudWindow { DataContext = vm };
            window.ApplyPreferences(preferences);
            window.Opened += async (_, _) =>
            {
                window.WindowState = WindowState.Normal;
                window.Activate();
                await vm.InitializeAsync();
            };
            desktop.MainWindow = window;
            window.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
