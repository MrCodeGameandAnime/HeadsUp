using System.Text.Json;
using GitCiHud.Models;

namespace GitCiHud.Services;

public sealed class SettingsService
{
    private readonly string _file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitCiHud", "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<UiPreferences> LoadAsync()
    {
        try { return JsonSerializer.Deserialize<UiPreferences>(await File.ReadAllTextAsync(_file), Json) ?? new UiPreferences(); }
        catch { return new UiPreferences(); }
    }
    public async Task SaveAsync(UiPreferences value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(value, Json));
    }
}
