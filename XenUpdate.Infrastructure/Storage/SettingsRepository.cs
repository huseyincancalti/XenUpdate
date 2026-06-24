using System.Text.Json;
using System.Text.Json.Serialization;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Storage;

/// <summary>
/// Persists application settings in %APPDATA%\XenUpdate\settings.json.
/// </summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XenUpdate");

    private static readonly string FilePath = Path.Combine(AppDataDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Serializes reads/writes so the frequent auto-save (theme, language, toggles) can't
    // interleave and corrupt the file.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(FilePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(AppDataDirectory);

            // Atomic write: a crash mid-write leaves the previous good file intact.
            var tempPath = FilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            Gate.Release();
        }
    }
}
