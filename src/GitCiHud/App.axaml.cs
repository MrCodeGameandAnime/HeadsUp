using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitCiHud.Services;
using GitCiHud.ViewModels;
using GitCiHud.Views;

namespace GitCiHud;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsService();
            var preferences = await settings.LoadAsync();
            var vm = new MainHudViewModel(new GitRepositoryService(), new GitHubActionsService(), settings, preferences);
            var window = new MainHudWindow { DataContext = vm };
            window.ApplyPreferences(preferences);
            desktop.MainWindow = window;
            await vm.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
