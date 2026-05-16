namespace XenUpdate.App.Messages;

/// <summary>
/// Sent via WeakReferenceMessenger whenever an update item is selected.
/// Triggers the "flying to cart" animation in MainWindow.
/// </summary>
public record UpdateSelectedMessage;
