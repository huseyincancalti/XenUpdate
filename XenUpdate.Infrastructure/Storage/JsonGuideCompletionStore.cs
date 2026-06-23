using System.Text.Json;
using XenUpdate.Core.Interfaces;

namespace XenUpdate.Infrastructure.Storage;

/// <summary>
/// Persists completed guide ids as a JSON string array in
/// <c>%APPDATA%\XenUpdate\guides-completed.json</c>.
/// </summary>
public sealed class JsonGuideCompletionStore : IGuideCompletionStore
{
    private readonly string _filePath;
    private readonly ILoggerService _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Initializes a new <see cref="JsonGuideCompletionStore"/>.</summary>
    public JsonGuideCompletionStore(ILoggerService logger)
    {
        _logger = logger;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XenUpdate");
        _filePath = Path.Combine(dir, "guides-completed.json");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> GetCompletedIdsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await LoadAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetCompletedAsync(string guideId, bool completed)
    {
        if (string.IsNullOrWhiteSpace(guideId))
            return;

        await _gate.WaitAsync();
        try
        {
            var ids = await LoadAsync();
            if (completed)
                ids.Add(guideId);
            else
                ids.Remove(guideId);

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(ids));
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save guide completion state.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HashSet<string>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var json = await File.ReadAllTextAsync(_filePath);
            var ids = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load guide completion state.", ex);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
