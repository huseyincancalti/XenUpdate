using Xunit;
using XenUpdate.Infrastructure.Hardware;

namespace XenUpdate.Tests.Hardware;

/// <summary>Pure-logic tests for NVIDIA driver version conversion and comparison (no WMI/network).</summary>
public sealed class NvidiaDriverServiceTests
{
    [Theory]
    [InlineData("32.0.16.1062", "610.62")]
    [InlineData("31.0.15.5612", "556.12")]
    [InlineData("31.0.15.6109", "561.09")]
    public void ToMarketingVersion_ConvertsWmiVersionToMarketing(string wmiVersion, string expected)
    {
        Assert.Equal(expected, NvidiaDriverService.ToMarketingVersion(wmiVersion));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.2")]
    [InlineData("")]
    public void ToMarketingVersion_ReturnsNull_ForUnparseable(string wmiVersion)
    {
        Assert.Null(NvidiaDriverService.ToMarketingVersion(wmiVersion));
    }

    [Theory]
    [InlineData("566.36", "566.14", true)]
    [InlineData("610.62", "610.62", false)]
    [InlineData("560.94", "566.14", false)]
    public void IsNewer_ComparesMarketingVersions(string latest, string installed, bool expected)
    {
        Assert.Equal(expected, NvidiaDriverService.IsNewer(latest, installed));
    }
}
