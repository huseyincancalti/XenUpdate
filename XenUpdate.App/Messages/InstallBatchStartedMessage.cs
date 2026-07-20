using XenUpdate.Core.Models;

namespace XenUpdate.App.Messages;

/// <summary>
/// Sent via WeakReferenceMessenger right before a page's install loop starts, carrying the
/// exact item instances about to be installed. The update queue window listens for this to pop
/// itself open and add a live group — it never re-derives the selection itself, since by the
/// time it could react, the page may already be mid-loop and Status/IsSelected have moved on.
/// </summary>
public sealed record InstallBatchStartedMessage(string SourceLabel, IReadOnlyList<UpdateItem> Items);
