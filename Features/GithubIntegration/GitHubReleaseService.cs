using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Base.Core;

namespace GithubIntegration;

public class GitHubReleaseService : WpfBehaviourSingleton<GitHubReleaseService>
{
	private HttpClient httpClient;
    private bool ownsHttpClient;

    public void Setup(string personalAccessToken = null, HttpClient httpClient = null)
    {
        ownsHttpClient = httpClient is null;
        this.httpClient = httpClient ?? new HttpClient();

        this.httpClient.DefaultRequestHeaders.UserAgent.Clear();
        this.httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("WpfGitHubReleaseClient", "1.0"));

        this.httpClient.DefaultRequestHeaders.Accept.Clear();
        this.httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        this.httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        if (!string.IsNullOrWhiteSpace(personalAccessToken))
        {
            this.httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", personalAccessToken);
        }
    }

    public async Task<IReadOnlyList<GitHubRelease>> GetLatestReleasesAsync(
        string owner,
        string repository,
        int count = 10,
        bool includeDrafts = false,
        bool includePrereleases = true,
        CancellationToken cancellationToken = default)
    {
        ValidateRepository(owner, repository);

        if (count <= 0)
            return [];

        var perPage = Math.Min(count, 100);
        var url =
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/" +
            $"{Uri.EscapeDataString(repository)}/releases?per_page={perPage}";

        using var response = await httpClient.GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub API request failed with status {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}). Response: {json}",
                null,
                response.StatusCode);
        }

        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(
            json,
            JsonOptions) ?? [];

        return [.. releases
            .Where(release => includeDrafts || !release.IsDraft)
            .Where(release => includePrereleases || !release.IsPrerelease)
            .OrderByDescending(release => release.PublishedAt ?? release.CreatedAt)
            .Take(count)];
    }

    public async Task<GitHubRelease> GetLatestReleaseAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken = default)
    {
        ValidateRepository(owner, repository);

        var url =
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/" +
            $"{Uri.EscapeDataString(repository)}/releases/latest";

        using var response = await httpClient.GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub API request failed with status {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}). Response: {json}",
                null,
                response.StatusCode);
        }

        return JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
    }

    private static void ValidateRepository(string owner, string repository)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Repository owner is required.", nameof(owner));

        if (string.IsNullOrWhiteSpace(repository))
            throw new ArgumentException("Repository name is required.", nameof(repository));
    }

    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public sealed class GitHubRelease
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Description { get; init; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = string.Empty;

    [JsonPropertyName("zipball_url")]
    public string ZipballUrl { get; init; } = string.Empty;

    [JsonPropertyName("tarball_url")]
    public string TarballUrl { get; init; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool IsDraft { get; init; }

    [JsonPropertyName("prerelease")]
    public bool IsPrerelease { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("author")]
    public GitHubUser Author { get; init; }

    [JsonPropertyName("assets")]
    public IReadOnlyList<GitHubReleaseAsset> Assets { get; init; } = [];
}

public sealed class GitHubReleaseAsset
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("download_count")]
    public int DownloadCount { get; init; }

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class GitHubUser
{
    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;

    [JsonPropertyName("avatar_url")]
    public string AvatarUrl { get; init; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = string.Empty;
}
}
