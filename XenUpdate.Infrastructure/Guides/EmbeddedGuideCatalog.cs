using System.Text.Json;
using System.Text.Json.Serialization;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Guides;

/// <summary>
/// Loads the guided-update catalog from a JSON file embedded in this assembly. Caches the
/// result for the app's lifetime. Designed so a remote/updatable catalog can replace it later
/// without touching callers.
/// </summary>
public sealed class EmbeddedGuideCatalog : IGuideCatalog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILoggerService _logger;
    private IReadOnlyList<GuideItem>? _cache;

    /// <summary>Initializes a new <see cref="EmbeddedGuideCatalog"/>.</summary>
    public EmbeddedGuideCatalog(ILoggerService logger) => _logger = logger;

    /// <inheritdoc />
    public Task<IReadOnlyList<GuideItem>> GetGuidesAsync()
    {
        if (_cache is not null)
            return Task.FromResult(_cache);

        try
        {
            var assembly = typeof(EmbeddedGuideCatalog).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("guides.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
            {
                _logger.Warning("Guide catalog resource not found; using an empty catalog.");
                _cache = Array.Empty<GuideItem>();
                return Task.FromResult(_cache);
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            var guides = JsonSerializer.Deserialize<List<GuideItem>>(stream, Options) ?? new List<GuideItem>();

            _cache = guides;
            _logger.Info($"Guide catalog loaded: {guides.Count} guide(s).");
            return Task.FromResult(_cache);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load the guide catalog.", ex);
            _cache = Array.Empty<GuideItem>();
            return Task.FromResult(_cache);
        }
    }
}
