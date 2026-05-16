namespace XenUpdate.App.Messages;

/// <summary>
/// Sent via WeakReferenceMessenger to display a toast notification via the main Snackbar.
/// </summary>
public record NotificationMessage(string Message);
