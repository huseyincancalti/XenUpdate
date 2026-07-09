using System.Text.Json;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Storage;

/// <summary>
/// Persists whitelisted update entries in %APPDATA%\XenUpdate\whitelist.json.
/// </summary>
public sealed class WhitelistRepository : IWhitelistRepository
{
    /// <inheritdoc />
    public event Action? WhitelistChanged;

    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XenUpdate");

    private static readonly string FilePath = Path.Combine(AppDataDirectory, "whitelist.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WhitelistEntry>> GetEntriesAsync()
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
    public async Task<IReadOnlyList<string>> GetWhitelistedIdsAsync(UpdateSource source)
    {
        var entries = await GetEntriesAsync().ConfigureAwait(false);
        return entries.Where(entry => entry.Source == source).Select(entry => entry.Id).ToList();
    }

    /// <inheritdoc />
    public async Task AddAsync(UpdateSource source, string id, string displayName)
    {
        var changed = false;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var normalized = Normalize(id);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var entries = await ReadEntriesUnsafeAsync().ConfigureAwait(false);
            if (entries.Any(entry => entry.Source == source && string.Equals(entry.Id, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            entries.Add(new WhitelistEntry
            {
                Source = source,
                Id = normalized,
                DisplayName = Normalize(displayName)
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
            WhitelistChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(UpdateSource source, string id)
    {
        var changed = false;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var normalized = Normalize(id);
            var entries = await ReadEntriesUnsafeAsync().ConfigureAwait(false);
            changed = entries.RemoveAll(entry =>
                entry.Source == source && string.Equals(entry.Id, normalized, StringComparison.OrdinalIgnoreCase)) > 0;

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
            WhitelistChanged?.Invoke();
        }
    }

    private static async Task<List<WhitelistEntry>> ReadEntriesUnsafeAsync()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(FilePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<WhitelistEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task WriteEntriesUnsafeAsync(List<WhitelistEntry> entries)
    {
        Directory.CreateDirectory(AppDataDirectory);

        var distinctEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => (entry.Source, Id: entry.Id.ToUpperInvariant()))
            .Select(group => group.First())
            .OrderBy(entry => entry.Source)
            .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var json = JsonSerializer.Serialize(distinctEntries, JsonOptions);
        await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
