namespace XenUpdate.App.Messages;

/// <summary>
/// Sent via WeakReferenceMessenger to display a native Windows balloon tip
/// from the system tray icon. Used when the main window is hidden.
/// </summary>
public record BalloonTipMessage(string Title, string Message);
