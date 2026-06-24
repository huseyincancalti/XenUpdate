using System.Net.NetworkInformation;

namespace XenUpdate.App.Services;

/// <summary>
/// Monitors the machine's network availability and raises <see cref="OnlineStatusChanged"/>
/// whenever connectivity flips. Used by <c>ShellViewModel</c> to dim the UI and show a
/// "No Internet" badge in the title bar while the network is down.
/// </summary>
public sealed class NetworkMonitorService : IDisposable
{
    /// <summary>True when at least one connected network interface is present.</summary>
    public bool IsOnline { get; private set; }

    /// <summary>Fired on a ThreadPool thread when the online state changes.</summary>
    public event Action<bool>? OnlineStatusChanged;

    public NetworkMonitorService()
    {
        IsOnline = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        var nowOnline = e.IsAvailable;
        if (nowOnline == IsOnline)
            return;

        IsOnline = nowOnline;
        OnlineStatusChanged?.Invoke(nowOnline);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }
}
