using System.Text.Json;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Storage;

/// <summary>
/// Reads and writes the blacklist JSON file stored at
/// <c>%APPDATA%\XenUpdate\blacklist.json</c>.
///
/// New entries are saved as a simple JSON array of objects:
/// <code>
/// [
///   { "packageId": "Microsoft.Teams", "reason": "Handled manually" }
/// ]
/// </code>
///
/// Older files that contain only package ID strings are still supported.
/// A <see cref="SemaphoreSlim"/> prevents concurrent file writes.
/// </summary>
public sealed class JsonBlacklistRepository : IBlacklistRepository
{
    /// <inheritdoc />
    public event Action? BlacklistChanged;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XenUpdate", "blacklist.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILoggerService _logger;

    /// <summary>Initializes the repository with the logger service.</summary>
    public JsonBlacklistRepository(ILoggerService logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlacklistEntry>> GetEntriesAsync()
    {
        if (!File.Exists(FilePath))
        {
            return Array.Empty<BlacklistEntry>();
        }

        await _lock.WaitAsync();
        try
        {
            return await ReadEntriesUnsafeAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not read blacklist file. Returning empty list. Reason: {ex.Message}");
            return Array.Empty<BlacklistEntry>();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetBlacklistedIdsAsync()
    {
        var entries = await GetEntriesAsync();
        return entries.Select(entry => entry.PackageId).ToList();
    }

    /// <inheritdoc />
    public async Task AddAsync(string packageId, string? reason = null)
    {
        var changed = false;

        await _lock.WaitAsync();
        try
        {
            var normalizedPackageId = NormalizePackageId(packageId);
            if (string.IsNullOrWhiteSpace(normalizedPackageId))
            {
                return;
            }

            var entries = await ReadEntriesUnsafeAsync();
            if (entries.Any(entry => string.Equals(entry.PackageId, normalizedPackageId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            entries.Add(new BlacklistEntry
            {
                PackageId = normalizedPackageId,
                Reason = NormalizeReason(reason)
            });

            await WriteEntriesUnsafeAsync(entries);
            _logger.Info($"Added '{normalizedPackageId}' to blacklist.");
            changed = true;
        }
        finally
        {
            _lock.Release();
        }

        // Raise the event after the semaphore is released so subscribers that
        // call GetEntriesAsync (which also acquires the semaphore) don't deadlock.
        if (changed)
        {
            BlacklistChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string packageId)
    {
        var changed = false;

        await _lock.WaitAsync();
        try
        {
            var normalizedPackageId = NormalizePackageId(packageId);
            if (string.IsNullOrWhiteSpace(normalizedPackageId))
            {
                return;
            }

            var entries = await ReadEntriesUnsafeAsync();
            var removed = entries.RemoveAll(entry =>
                string.Equals(entry.PackageId, normalizedPackageId, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                await WriteEntriesUnsafeAsync(entries);
                _logger.Info($"Removed '{normalizedPackageId}' from blacklist.");
                changed = true;
            }
        }
        finally
        {
            _lock.Release();
        }

        // Raise after the semaphore is released for the same reason as AddAsync.
        if (changed)
        {
            BlacklistChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await WriteEntriesUnsafeAsync([]);
            _logger.Info("Blacklist cleared.");
        }
        finally
        {
            _lock.Release();
        }

        BlacklistChanged?.Invoke();
    }

    private async Task<List<BlacklistEntry>> ReadEntriesUnsafeAsync()
    {
        if (!File.Exists(FilePath))
        {
            return new List<BlacklistEntry>();
        }

        var json = await File.ReadAllTextAsync(FilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<BlacklistEntry>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<BlacklistEntry>();
            }

            var entries = new List<BlacklistEntry>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var entry = ParseEntry(element);
                if (entry is null)
                {
                    continue;
                }

                if (entries.Any(existing => string.Equals(existing.PackageId, entry.PackageId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                entries.Add(entry);
            }

            return entries;
        }
        catch
        {
            return new List<BlacklistEntry>();
        }
    }

    private static BlacklistEntry? ParseEntry(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var packageId = NormalizePackageId(element.GetString());
            return string.IsNullOrWhiteSpace(packageId)
                ? null
                : new BlacklistEntry { PackageId = packageId };
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetPropertyCaseInsensitive(element, "packageId", out var packageIdProperty))
        {
            return null;
        }

        var packageIdValue = NormalizePackageId(packageIdProperty.GetString());
        if (string.IsNullOrWhiteSpace(packageIdValue))
        {
            return null;
        }

        var reasonValue = TryGetPropertyCaseInsensitive(element, "reason", out var reasonProperty)
            ? NormalizeReason(reasonProperty.GetString())
            : string.Empty;

        return new BlacklistEntry
        {
            PackageId = packageIdValue,
            Reason = reasonValue
        };
    }

    private async Task WriteEntriesUnsafeAsync(List<BlacklistEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        var payload = entries
            .OrderBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new BlacklistFileEntry
            {
                PackageId = entry.PackageId,
                Reason = entry.Reason
            })
            .ToList();

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(FilePath, json);
    }

    private static string NormalizePackageId(string? packageId)
    {
        return packageId?.Trim() ?? string.Empty;
    }

    private static string NormalizeReason(string? reason)
    {
        return reason?.Trim() ?? string.Empty;
    }

    private static bool TryGetPropertyCaseInsensitive(
        JsonElement element,
        string propertyName,
        out JsonElement propertyValue)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                propertyValue = property.Value;
                return true;
            }
        }

        propertyValue = default;
        return false;
    }

    private sealed class BlacklistFileEntry
    {
        public string PackageId { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }
}
