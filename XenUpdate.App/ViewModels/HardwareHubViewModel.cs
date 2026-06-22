using CommunityToolkit.Mvvm.ComponentModel;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// Hardware Hub page: detected CPU/GPU profile. Foundation for the device-specific
/// guided-update center (matching detected hardware to manufacturer update guides).
/// </summary>
public sealed partial class HardwareHubViewModel : ObservableObject
{
    private readonly IHardwareScannerService _hardwareScanner;

    [ObservableProperty]
    private HardwareProfile _hardware = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public HardwareHubViewModel(IHardwareScannerService hardwareScanner)
    {
        _hardwareScanner = hardwareScanner;
    }

    /// <summary>Resolves the CPU/GPU profile off the UI thread. Called from the view's Loaded event.</summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Detecting hardware…";

        try
        {
            Hardware = await _hardwareScanner.GetCurrentHardwareAsync();
            StatusMessage = string.IsNullOrWhiteSpace(Hardware.GpuVendor)
                ? "Hardware detected."
                : $"Detected {Hardware.GpuVendor} GPU.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hardware detection failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
