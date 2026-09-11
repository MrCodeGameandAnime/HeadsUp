using System.Diagnostics;

namespace GitCiHud.Services;

internal static class ProcessRunner
{
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string fileName, string arguments, string? workingDirectory, CancellationToken ct)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(fileName, arguments) { WorkingDirectory = workingDirectory ?? "", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return (process.ExitCode, (await output).Trim(), (await error).Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return (-1, "", $"{fileName} unavailable: {ex.Message}");
        }
    }
}
