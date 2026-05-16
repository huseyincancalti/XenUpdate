namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Creates Windows System Restore Points before installing updates.
/// </summary>
public interface ISystemRestoreService
{
    /// <summary>
    /// Creates a system restore point with the given description.
    /// Returns true if successful, false if creation failed or was skipped.
    /// </summary>
    Task<bool> CreateRestorePointAsync(string description);
}
