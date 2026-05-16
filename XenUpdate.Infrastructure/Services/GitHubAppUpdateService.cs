using System.Net.Http.Json;
using System.Text.Json.Serialization;
using XenUpdate.Core.Interfaces;

namespace XenUpdate.Infrastructure.Services;

/// <summary>
/// Checks the GitHub Releases API to determine whether a newer version of XenUpdate
/// is available at <c>huseyincancalti/XenUpdate</c>.
/// </summary>
public sealed class GitHubAppUpdateService : IAppUpdateService, IDisposable
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/huseyincancalti/XenUpdate/releases/latest";

    private readonly HttpClient _httpClient;

    /// <summary>Initializes a new <see cref="GitHubAppUpdateService"/> with a configured <see cref="HttpClient"/>.</summary>
    public GitHubAppUpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XenUpdate-App");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <inheritdoc />
    public async Task<(bool HasUpdate, string UpdateUrl)> CheckForAppUpdatesAsync(string currentVersion)
    {
        try
        {
            var release = await _httpClient
                .GetFromJsonAsync<GitHubRelease>(ReleasesApiUrl)
                .ConfigureAwait(false);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return (false, string.Empty);
            }

            var tagVersion = release.TagName.TrimStart('v', 'V').Trim();

            if (!Version.TryParse(tagVersion, out var remoteVersion) ||
                !Version.TryParse(currentVersion, out var localVersion))
            {
                return (false, string.Empty);
            }

            var hasUpdate = remoteVersion > localVersion;
            return (hasUpdate, hasUpdate ? release.HtmlUrl ?? string.Empty : string.Empty);
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}
