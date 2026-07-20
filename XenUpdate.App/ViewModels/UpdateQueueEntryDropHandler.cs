using GongSolutions.Wpf.DragDrop;
using XenUpdate.Core.Enums;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// Drag-and-drop reorder rules for the flat update queue list. Built on
/// GongSolutions.WPF.DragDrop (MIT-licensed, https://github.com/punker76/gong-wpf-dragdrop)
/// rather than hand-rolled DragDrop.DoDragDrop plumbing — the library brings the insertion-line
/// indicator, drag adorner, and correct drop-index semantics that make this feel like Steam's
/// download queue instead of a bare swap. <see cref="DefaultDropHandler"/> already implements
/// same-collection move; the only app-specific rule is the validation below.
/// </summary>
public sealed class UpdateQueueEntryDropHandler : DefaultDropHandler
{
    /// <summary>
    /// Both the dragged row and the row it's dropped near must still be Pending — reordering an
    /// item that's already installing or finished wouldn't do anything (ShellViewModel's loop
    /// only consults live order for items it hasn't started yet) and would just be confusing to
    /// allow. Leaving dropInfo.Effects at its default (None) for those cases means the drop
    /// indicator simply never appears there. Any Pending row can move anywhere among the other
    /// Pending rows, regardless of which source page it came from — the queue is one flat list.
    /// </summary>
    public override void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is UpdateQueueEntry source
            && source.Item.Status == UpdateStatus.Pending
            && (dropInfo.TargetItem is not UpdateQueueEntry target || target.Item.Status == UpdateStatus.Pending))
        {
            base.DragOver(dropInfo);
        }
    }
}
