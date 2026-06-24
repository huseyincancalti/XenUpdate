using Xunit;
using XenUpdate.Infrastructure.Hardware;

namespace XenUpdate.Tests.Hardware;

/// <summary>
/// Pure-logic tests for GPU selection/vendor parsing (no WMI access).
/// </summary>
public sealed class HardwareScannerServiceTests
{
    [Fact]
    public void PickPrimaryGpu_HybridLaptop_PrefersDiscreteNvidiaOverIntel()
    {
        // The exact case of an Intel + NVIDIA laptop, where WMI may list Intel first.
        var gpus = new[] { "Intel(R) Iris(R) Xe Graphics", "NVIDIA GeForce RTX 3050 Laptop GPU" };

        Assert.Equal("NVIDIA GeForce RTX 3050 Laptop GPU", HardwareScannerService.PickPrimaryGpu(gpus));
    }

    [Fact]
    public void PickPrimaryGpu_HybridLaptop_PrefersDiscreteAmdOverIntel()
    {
        var gpus = new[] { "Intel(R) UHD Graphics", "AMD Radeon RX 6700M" };

        Assert.Equal("AMD Radeon RX 6700M", HardwareScannerService.PickPrimaryGpu(gpus));
    }

    [Fact]
    public void PickPrimaryGpu_IntegratedOnly_FallsBackToFirst()
    {
        var gpus = new[] { "Intel(R) Iris(R) Xe Graphics" };

        Assert.Equal("Intel(R) Iris(R) Xe Graphics", HardwareScannerService.PickPrimaryGpu(gpus));
    }

    [Fact]
    public void PickPrimaryGpu_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HardwareScannerService.PickPrimaryGpu(System.Array.Empty<string>()));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3050 Laptop GPU", "NVIDIA")]
    [InlineData("AMD Radeon RX 6700M", "AMD")]
    [InlineData("ATI Radeon HD 5000", "AMD")]
    [InlineData("Intel(R) Iris(R) Xe Graphics", "Intel")]
    [InlineData("Some Unknown Display Adapter", "Unknown")]
    [InlineData("", "Unknown")]
    public void ParseGpuVendor_ReturnsExpectedVendor(string gpuName, string expected)
    {
        Assert.Equal(expected, HardwareScannerService.ParseGpuVendor(gpuName));
    }
}
