using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Supplies the catalog of guided manual-update entries. The current implementation reads an
/// embedded JSON file; the interface keeps the door open for a remotely-updated catalog later.
/// </summary>
public interface IGuideCatalog
{
    /// <summary>Returns every guide definition in the catalog.</summary>
    Task<IReadOnlyList<GuideItem>> GetGuidesAsync();
}
