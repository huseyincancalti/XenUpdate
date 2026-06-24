using Xunit;
using XenUpdate.Infrastructure.Winget;

namespace XenUpdate.Tests.Winget;

/// <summary>
/// Tests for <see cref="WingetProgressParser"/>, using lines shaped like winget's real
/// carriage-return progress output.
/// </summary>
public sealed class WingetProgressParserTests
{
    [Theory]
    [InlineData("  █████████▒▒▒▒  1024 KB / 3.07 MB", "1024 KB / 3.07 MB")]
    [InlineData("  ███████████████████▒  2.00 MB / 3.07 MB", "2.00 MB / 3.07 MB")]
    [InlineData("  ██████████████████████████████  3.07 MB / 3.07 MB", "3.07 MB / 3.07 MB")]
    [InlineData("950 B / 4.2 GB", "950 B / 4.2 GB")]
    public void TryParseDownload_ExtractsNormalizedSize(string line, string expected)
    {
        Assert.True(WingetProgressParser.TryParseDownload(line, out var text));
        Assert.Equal(expected, text);
    }

    [Fact]
    public void TryParseDownload_NormalizesUnitCasing()
    {
        Assert.True(WingetProgressParser.TryParseDownload("2 kb / 5 mb", out var text));
        Assert.Equal("2 KB / 5 MB", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Found PowerToys [Microsoft.PowerToys]")]
    [InlineData("Version: 0.100.0")]
    [InlineData("  ██████  42%")]
    public void TryParseDownload_ReturnsFalse_WhenNoSizePair(string line)
    {
        Assert.False(WingetProgressParser.TryParseDownload(line, out var text));
        Assert.Equal(string.Empty, text);
    }

    [Theory]
    [InlineData("  ███▒▒▒▒▒▒▒▒▒▒  10%", 10)]
    [InlineData("  ██████████████████████████████  100%", 100)]
    [InlineData("  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒  0%", 0)]
    public void TryParsePercent_ExtractsPercentage(string line, int expected)
    {
        Assert.True(WingetProgressParser.TryParsePercent(line, out var percent));
        Assert.Equal(expected, percent);
    }

    [Fact]
    public void TryParsePercent_TakesLastTokenOnLine()
    {
        Assert.True(WingetProgressParser.TryParsePercent("10%  37%  56%", out var percent));
        Assert.Equal(56, percent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Installing...")]
    [InlineData("2.00 MB / 3.07 MB")]
    public void TryParsePercent_ReturnsFalse_WhenNoPercent(string line)
    {
        Assert.False(WingetProgressParser.TryParsePercent(line, out var percent));
        Assert.Equal(-1, percent);
    }
}
