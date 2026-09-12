using Microsoft.Win32;

namespace GitCiHud.Services;

/// <summary>
/// Controls the per-user Windows startup entry. Other platforms simply retain
/// the preference without attempting to modify a platform-specific launcher.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HeadsUp";

    /// <summary>
    /// MSIX desktop apps have a package-aware startup-task API. Until HeadsUp
    /// uses that API, do not claim that the unpackaged registry setting works
    /// for an executable running from WindowsApps.
    /// </summary>
    public bool IsPackaged => OperatingSystem.IsWindows()
        && (IsWindowsAppsPath(Environment.ProcessPath) || IsWindowsAppsPath(AppContext.BaseDirectory));

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows() || IsPackaged) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return;

            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executable))
                    key.SetValue(ValueName, $"\"{executable}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Startup integration is optional. Keep the app usable if policy
            // or permissions prevent editing the per-user registry key.
        }
    }

    private static bool IsWindowsAppsPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
}
