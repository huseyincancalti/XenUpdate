using Xunit;
using XenUpdate.Infrastructure.Pip;

namespace XenUpdate.Tests.Pip;

/// <summary>
/// Unit tests for <see cref="PipListOutputParser"/>.
///
/// All tests are pure: no I/O, no mocking, no process spawning.
/// A string goes in; a list comes out.
/// </summary>
public sealed class PipListOutputParserTests
{
    private readonly PipListOutputParser _parser = new();

    private const string NormalOutput = """
        [
          {"name": "requests", "version": "2.31.0", "latest_version": "2.32.0", "latest_filetype": "wheel"},
          {"name": "numpy", "version": "1.26.0", "latest_version": "2.0.0", "latest_filetype": "wheel"},
          {"name": "flask", "version": "3.0.0", "latest_version": "3.1.0", "latest_filetype": "wheel"}
        ]
        """;

    [Fact]
    public void Parse_WithNormalOutput_ReturnsAllValidItems()
    {
        var results = _parser.Parse(NormalOutput);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void Parse_WithNormalOutput_CorrectlyMapsFirstItem()
    {
        var results = _parser.Parse(NormalOutput);
        var first = results[0];

        Assert.Equal("requests", first.PackageName);
        Assert.Equal("requests", first.DisplayName);
        Assert.Equal("2.31.0", first.CurrentVersion);
        Assert.Equal("2.32.0", first.AvailableVersion);
    }

    [Fact]
    public void Parse_ResultItems_HaveSourceSetToPip()
    {
        var results = _parser.Parse(NormalOutput);

        Assert.All(results, item =>
            Assert.Equal(Core.Enums.UpdateSource.Pip, item.Source));
    }

    [Fact]
    public void Parse_WithEmptyString_ReturnsEmptyList()
    {
        var results = _parser.Parse(string.Empty);

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_WithWhitespaceOnly_ReturnsEmptyList()
    {
        var results = _parser.Parse("   \n\n\r\n   ");

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_WithEmptyJsonArray_ReturnsEmptyList()
    {
        var results = _parser.Parse("[]");

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_WithMalformedJson_ReturnsEmptyListInsteadOfThrowing()
    {
        // e.g. a pip warning banner printed to stdout ahead of the JSON, or a broken
        // pip installation — the scanner must degrade gracefully, not crash the app.
        var results = _parser.Parse("WARNING: pip is being invoked by an old script wrapper");

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_EntryMissingLatestVersion_IsSkipped()
    {
        const string missingLatest = """
            [
              {"name": "goodpkg", "version": "1.0.0", "latest_version": "1.1.0"},
              {"name": "badpkg", "version": "2.0.0", "latest_version": ""}
            ]
            """;

        var results = _parser.Parse(missingLatest);

        Assert.Single(results);
        Assert.Equal("goodpkg", results[0].PackageName);
    }

    [Fact]
    public void Parse_EntryMissingVersion_FallsBackToUnknown()
    {
        const string missingVersion = """
            [
              {"name": "freshpkg", "version": "", "latest_version": "1.0.0"}
            ]
            """;

        var results = _parser.Parse(missingVersion);

        Assert.Single(results);
        Assert.Equal("Unknown", results[0].CurrentVersion);
    }
}
