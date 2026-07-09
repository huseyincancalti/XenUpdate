using Xunit;
using XenUpdate.Infrastructure.Hardware;

namespace XenUpdate.Tests.Hardware;

/// <summary>Pure-logic tests for Visual Studio version parsing and comparison (no vswhere/network).</summary>
public sealed class VisualStudioUpdateServiceTests
{
    [Fact]
    public void ParseInstalledInstance_ReadsFieldsFromVswhereJson()
    {
        const string json = """
        [
          {
            "productId": "Microsoft.VisualStudio.Product.Community",
            "channelUri": "https://aka.ms/vs/17/release/channel",
            "installationVersion": "17.11.35327.3"
          }
        ]
        """;

        var instance = VisualStudioUpdateService.ParseInstalledInstance(json);

        Assert.NotNull(instance);
        Assert.Equal("Microsoft.VisualStudio.Product.Community", instance!.Value.ProductId);
        Assert.Equal("https://aka.ms/vs/17/release/channel", instance.Value.ChannelUri);
        Assert.Equal("17.11.35327.3", instance.Value.InstalledVersion);
    }

    [Fact]
    public void ParseInstalledInstance_ReturnsNull_WhenArrayIsEmpty()
    {
        Assert.Null(VisualStudioUpdateService.ParseInstalledInstance("[]"));
    }

    [Fact]
    public void ParseInstalledInstance_ReturnsNull_WhenRequiredFieldsMissing()
    {
        const string json = """[{ "channelUri": "https://aka.ms/vs/17/release/channel" }]""";

        Assert.Null(VisualStudioUpdateService.ParseInstalledInstance(json));
    }

    [Fact]
    public void FindLatestVersion_FindsMatchingProductIdAnywhereInDocument()
    {
        const string json = """
        {
          "channelItems": [
            { "id": "Microsoft.VisualStudio.Product.Professional", "version": "17.12.99999.1" },
            { "id": "Microsoft.VisualStudio.Product.Community", "version": "17.12.35527.113" }
          ]
        }
        """;

        var latest = VisualStudioUpdateService.FindLatestVersion(json, "Microsoft.VisualStudio.Product.Community");

        Assert.Equal("17.12.35527.113", latest);
    }

    [Fact]
    public void FindLatestVersion_ReturnsNull_WhenProductIdNotPresent()
    {
        const string json = """{ "channelItems": [ { "id": "Microsoft.VisualStudio.Product.Enterprise", "version": "17.12.1.1" } ] }""";

        Assert.Null(VisualStudioUpdateService.FindLatestVersion(json, "Microsoft.VisualStudio.Product.Community"));
    }

    [Theory]
    [InlineData("17.12.35527.113", "17.11.35327.3", true)]
    [InlineData("17.11.35327.3", "17.11.35327.3", false)]
    [InlineData("17.10.1.1", "17.11.35327.3", false)]
    public void IsNewer_ComparesVersions(string latest, string installed, bool expected)
    {
        Assert.Equal(expected, VisualStudioUpdateService.IsNewer(latest, installed));
    }
}
