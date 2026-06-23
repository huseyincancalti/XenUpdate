namespace XenUpdate.Core.Interfaces;

/// <summary>Persists which guides the user has marked as completed.</summary>
public interface IGuideCompletionStore
{
    /// <summary>Returns the ids of all guides marked completed.</summary>
    Task<IReadOnlyCollection<string>> GetCompletedIdsAsync();

    /// <summary>Marks a guide completed or not completed and persists the change.</summary>
    Task SetCompletedAsync(string guideId, bool completed);
}
