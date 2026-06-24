using Xunit;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;
using XenUpdate.Infrastructure.Guides;

namespace XenUpdate.Tests.Guides;

/// <summary>
/// Guards the embedded guide catalog JSON against typos / breakage (it deserializes correctly
/// and the expected GPU-vendor guides are present).
/// </summary>
public sealed class EmbeddedGuideCatalogTests
{
    private sealed class NullLogger : ILoggerService
    {
        public event Action<LogEntry>? LogEntryAdded { add { } remove { } }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }

    private readonly IGuideCatalog _catalog = new EmbeddedGuideCatalog(new NullLogger());

    [Fact]
    public async Task GetGuidesAsync_LoadsWellFormedGuides()
    {
        var guides = await _catalog.GetGuidesAsync("en");

        Assert.NotEmpty(guides);
        Assert.All(guides, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Id));
            Assert.False(string.IsNullOrWhiteSpace(g.Title));
            Assert.False(string.IsNullOrWhiteSpace(g.OfficialUrl));
            Assert.NotEmpty(g.Steps);
        });
    }

    [Fact]
    public async Task GetGuidesAsync_IncludesNvidiaGraphicsDriverGuide()
    {
        var guides = await _catalog.GetGuidesAsync("en");

        var nvidia = Assert.Single(guides, g => g.RequiredGpuVendor == "NVIDIA");
        Assert.Equal(GuideCategory.GraphicsDriver, nvidia.Category);
        Assert.Contains("nvidia", nvidia.OfficialUrl, StringComparison.OrdinalIgnoreCase);
    }
}
