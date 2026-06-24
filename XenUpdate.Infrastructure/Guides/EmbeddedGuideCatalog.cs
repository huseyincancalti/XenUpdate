using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Guides;

/// <summary>
/// Loads the guided-update catalog from per-language JSON files embedded in this assembly
/// (<c>guides.{lang}.json</c>, falling back to English). Caches each language for the app's
/// lifetime. Designed so a remote/updatable catalog can replace it later without touching callers.
/// </summary>
public sealed class EmbeddedGuideCatalog : IGuideCatalog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILoggerService _logger;
    private readonly ConcurrentDictionary<string, IReadOnlyList<GuideItem>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new <see cref="EmbeddedGuideCatalog"/>.</summary>
    public EmbeddedGuideCatalog(ILoggerService logger) => _logger = logger;

    /// <inheritdoc />
    public Task<IReadOnlyList<GuideItem>> GetGuidesAsync(string languageCode)
    {
        var lang = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
        return Task.FromResult(_cache.GetOrAdd(lang, LoadForLanguage));
    }

    private IReadOnlyList<GuideItem> LoadForLanguage(string lang) =>
        Load($"guides.{lang}.json") ?? Load("guides.en.json") ?? Array.Empty<GuideItem>();

    private IReadOnlyList<GuideItem>? Load(string fileSuffix)
    {
        try
        {
            var assembly = typeof(EmbeddedGuideCatalog).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
                return null;

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            var guides = JsonSerializer.Deserialize<List<GuideItem>>(stream, Options) ?? new List<GuideItem>();
            _logger.Info($"Guide catalog '{fileSuffix}' loaded: {guides.Count} guide(s).");
            return guides;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load guide catalog '{fileSuffix}'.", ex);
            return null;
        }
    }
}
