using Xunit;
using XenUpdate.Infrastructure.Winget;

namespace XenUpdate.Tests.Winget;

/// <summary>
/// Unit tests for <see cref="WingetOutputParser"/>.
///
/// All tests are pure: no I/O, no mocking, no process spawning.
/// A string goes in; a list comes out.
///
/// Run with: dotnet test
/// </summary>
public sealed class WingetOutputParserTests
{
    private readonly WingetOutputParser _parser = new();

    // -------------------------------------------------------------------------
    // Shared realistic sample strings
    // -------------------------------------------------------------------------

    /// <summary>
    /// Typical output of "winget upgrade" with four upgradeable packages.
    /// Column positions match what winget actually produces (spaces as padding).
    /// </summary>
    private const string NormalOutput = """
        Name                                      Id                                    Version         Available       Source
        ----------------------------------------------------------------------------------------------------------------------
        7-Zip 24.09 (x64)                         7zip.7zip                             24.09.00.0      25.00.00.0      winget
        Amazon Kindle                              Amazon.Kindle                         2.4.0.200       2.5.0.100       winget
        Git                                        Git.Git                               2.44.0          2.45.2.2        winget
        Microsoft Visual Studio Code               Microsoft.VisualStudioCode            1.87.2          1.88.1          winget
        4 upgrades available.
        """;

    /// <summary>
    /// Output with one well-formed row and one row with missing fields (no available version).
    /// The malformed row should be silently skipped.
    /// </summary>
    private const string MixedOutput = """
        Name                                      Id                                    Version         Available       Source
        ----------------------------------------------------------------------------------------------------------------------
        Good App                                   GoodPublisher.GoodApp                 1.0.0           2.0.0           winget
        Bad Row With No Fields
        Another Bad                                                                                                       
        Git                                        Git.Git                               2.44.0          2.45.2.2        winget
        3 upgrades available.
        """;

    /// <summary>
    /// Output where one package has "Unknown" as the available version.
    /// The parser should skip that row since there is nothing to upgrade to.
    /// </summary>
    private const string WithUnknownVersion = """
        Name                                      Id                                    Version         Available       Source
        ----------------------------------------------------------------------------------------------------------------------
        Normal App                                 Normal.App                            1.0.0           2.0.0           winget
        Mystery App                                Mystery.App                           3.1.4           Unknown         winget
        2 upgrades available.
        """;

    /// <summary>
    /// Realistic output decorated with ANSI color codes that winget emits on some terminals.
    /// The escape sequences are inserted BEFORE the content so that after stripping them
    /// the remaining characters still align with the header column positions.
    /// </summary>
    private const string AnsiDecoratedOutput =
        "Name                                      Id                                    Version         Available       Source\r\n" +
        "----------------------------------------------------------------------------------------------------------------------\r\n" +
        "\x1b[32mColored App\x1b[0m                               Colored.App                           1.0.0           2.0.0           winget\r\n" +
        "1 upgrades available.\r\n";

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithNormalOutput_ReturnsAllValidItems()
    {
        var results = _parser.Parse(NormalOutput);

        Assert.Equal(4, results.Count);
    }

    [Fact]
    public void Parse_WithNormalOutput_CorrectlyMapsFirstItem()
    {
        var results = _parser.Parse(NormalOutput);
        var first = results[0];

        Assert.Equal("7zip.7zip",         first.WingetPackageId);
        Assert.Equal("24.09.00.0",        first.CurrentVersion);
        Assert.Equal("25.00.00.0",        first.AvailableVersion);
        Assert.False(string.IsNullOrWhiteSpace(first.DisplayName));
    }

    [Fact]
    public void Parse_WithNormalOutput_AllItemsHavePackageId()
    {
        var results = _parser.Parse(NormalOutput);

        Assert.All(results, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.WingetPackageId)));
    }

    [Fact]
    public void Parse_WithNormalOutput_AllItemsHaveAvailableVersion()
    {
        var results = _parser.Parse(NormalOutput);

        Assert.All(results, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.AvailableVersion)));
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
    public void Parse_WithNoHeaderLine_ReturnsEmptyList()
    {
        const string noHeader = """
            This is some text without a winget table header.
            Just random lines that cannot be parsed.
            """;

        var results = _parser.Parse(noHeader);

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_WithMixedOutput_SkipsMalformedRows()
    {
        var results = _parser.Parse(MixedOutput);

        // Only "Good App" and "Git" are parseable; malformed rows are skipped.
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Parse_WithMixedOutput_ValidRowsAreCorrect()
    {
        var results = _parser.Parse(MixedOutput);

        Assert.Contains(results, r => r.WingetPackageId == "GoodPublisher.GoodApp");
        Assert.Contains(results, r => r.WingetPackageId == "Git.Git");
    }

    [Fact]
    public void Parse_RowWithUnknownAvailableVersion_IsSkipped()
    {
        var results = _parser.Parse(WithUnknownVersion);

        // "Mystery App" has "Unknown" as Available — should be excluded.
        Assert.Single(results);
        Assert.Equal("Normal.App", results[0].WingetPackageId);
    }

    [Fact]
    public void Parse_WithAnsiEscapeCodes_StillParsesCorrectly()
    {
        var results = _parser.Parse(AnsiDecoratedOutput);

        Assert.Single(results);
        Assert.Equal("Colored.App", results[0].WingetPackageId);
        Assert.Equal("2.0.0", results[0].AvailableVersion);
    }

    [Fact]
    public void Parse_WithSingleValidRow_ReturnsOneItem()
    {
        const string singleRow = """
            Name                                      Id                                    Version         Available       Source
            ----------------------------------------------------------------------------------------------------------------------
            OnlyApp                                    Publisher.OnlyApp                     9.0             9.1             winget
            1 upgrades available.
            """;

        var results = _parser.Parse(singleRow);

        Assert.Single(results);
    }

    [Fact]
    public void Parse_ResultItems_HaveSourceSetToWinget()
    {
        var results = _parser.Parse(NormalOutput);

        Assert.All(results, item =>
            Assert.Equal(Core.Enums.UpdateSource.Winget, item.Source));
    }

    [Fact]
    public void Parse_WithFooterLineOnly_ReturnsEmptyList()
    {
        const string footerOnly = """
            Name                                      Id                                    Version         Available       Source
            ----------------------------------------------------------------------------------------------------------------------
            No applicable upgrades found.
            """;

        var results = _parser.Parse(footerOnly);

        Assert.Empty(results);
    }

    // -------------------------------------------------------------------------
    // Locale / format robustness (position-based parsing)
    // -------------------------------------------------------------------------

    // Builds a fixed-width row so the header and data columns line up exactly,
    // independent of how wide any particular field is.
    private static string Row(string name, string id, string version, string available, string source) =>
        name.PadRight(28) + id.PadRight(26) + version.PadRight(16) + available.PadRight(18) + source;

    [Fact]
    public void Parse_WithLocalizedTurkishHeader_ParsesByColumnPosition()
    {
        // Turkish Windows localizes the winget column headers. Matching the literal word
        // "Available" would fail and return nothing; columns must be found by position.
        var output = string.Join("\n",
            Row("Ad", "Kimlik", "Sürüm", "Kullanılabilir", "Kaynak"),
            new string('-', 110),
            Row("Örnek Uygulama", "Ornek.App", "1.0.0", "2.0.0", "winget"),
            "1 yükseltme kullanılabilir.");

        var results = _parser.Parse(output);

        Assert.Single(results);
        Assert.Equal("Ornek.App", results[0].WingetPackageId);
        Assert.Equal("2.0.0", results[0].AvailableVersion);
    }

    [Fact]
    public void Parse_WithoutSourceColumn_StillParses()
    {
        var output = string.Join("\n",
            "Name".PadRight(28) + "Id".PadRight(26) + "Version".PadRight(16) + "Available",
            new string('-', 90),
            "My App".PadRight(28) + "My.App".PadRight(26) + "1.0".PadRight(16) + "1.5",
            "1 upgrades available.");

        var results = _parser.Parse(output);

        Assert.Single(results);
        Assert.Equal("My.App", results[0].WingetPackageId);
        Assert.Equal("1.5", results[0].AvailableVersion);
    }

    [Fact]
    public void Parse_WithWiderColumnSpacing_StillParses()
    {
        // Different winget builds pad columns to different widths; parsing must adapt.
        var output = string.Join("\n",
            "Name".PadRight(40) + "Id".PadRight(40) + "Version".PadRight(20) + "Available".PadRight(20) + "Source",
            new string('-', 130),
            "Wide Spacing App".PadRight(40) + "Wide.App".PadRight(40) + "3.0".PadRight(20) + "3.1".PadRight(20) + "winget",
            "1 upgrades available.");

        var results = _parser.Parse(output);

        Assert.Single(results);
        Assert.Equal("Wide.App", results[0].WingetPackageId);
        Assert.Equal("3.1", results[0].AvailableVersion);
    }
}
