namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Checks GitHub releases to determine whether a newer version of XenUpdate is available.
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// Queries the GitHub Releases API and compares the latest release tag with
    /// <paramref name="currentVersion"/>.
    /// </summary>
    /// <returns>
    /// A tuple where <c>HasUpdate</c> is <see langword="true"/> when a newer release exists,
    /// and <c>UpdateUrl</c> is the HTML URL of that release page.
    /// </returns>
    Task<(bool HasUpdate, string UpdateUrl)> CheckForAppUpdatesAsync(string currentVersion);
}
