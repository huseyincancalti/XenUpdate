using System.Text.Json;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Storage;

/// <summary>
/// Persists ignored update entries in %APPDATA%\XenUpdate\blacklist.json.
/// </summary>
public sealed class BlacklistRepository : IBlacklistRepository
{
    /// <inheritdoc />
    public event Action? BlacklistChanged;

    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XenUpdate");

    private static readonly string FilePath = Path.Combine(AppDataDirectory, "blacklist.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlacklistEntry>> GetEntriesAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await ReadEntriesUnsafeAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetBlacklistedIdsAsync()
    {
        var entries = await GetEntriesAsync().ConfigureAwait(false);
        return entries.Select(entry => entry.PackageId).ToList();
    }

    /// <inheritdoc />
    public async Task AddAsync(string packageId, string? reason = null)
    {
        var changed = false;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var normalized = Normalize(packageId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var entries = await ReadEntriesUnsafeAsync().ConfigureAwait(false);
            if (entries.Any(entry => string.Equals(entry.PackageId, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            entries.Add(new BlacklistEntry
            {
                PackageId = normalized,
                Reason = Normalize(reason)
            });

            await WriteEntriesUnsafeAsync(entries).ConfigureAwait(false);
            changed = true;
        }
        finally
        {
            _lock.Release();
        }

        if (changed)
        {
            BlacklistChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string packageId)
    {
        var changed = false;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var normalized = Normalize(packageId);
            var entries = await ReadEntriesUnsafeAsync().ConfigureAwait(false);
            changed = entries.RemoveAll(entry =>
                string.Equals(entry.PackageId, normalized, StringComparison.OrdinalIgnoreCase)) > 0;

            if (changed)
            {
                await WriteEntriesUnsafeAsync(entries).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (changed)
        {
            BlacklistChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await WriteEntriesUnsafeAsync([]).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        BlacklistChanged?.Invoke();
    }

    private static async Task<List<BlacklistEntry>> ReadEntriesUnsafeAsync()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(FilePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<BlacklistEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task WriteEntriesUnsafeAsync(List<BlacklistEntry> entries)
    {
        Directory.CreateDirectory(AppDataDirectory);

        var distinctEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.PackageId))
            .GroupBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var json = JsonSerializer.Serialize(distinctEntries, JsonOptions);
        await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
