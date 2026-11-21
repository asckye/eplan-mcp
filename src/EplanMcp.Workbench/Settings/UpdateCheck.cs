using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EplanMcp.Workbench.Settings;

internal enum UpdateState
{
    Newer,
    Current,
    Failed,
}

/// <summary>What one check found: a newer release with its page, this one current, or why it could not tell.</summary>
internal sealed record UpdateResult(UpdateState State, string Latest, string Page, string Reason);

/// <summary>
/// The update check (ADR-0066): only when the engineer clicks, one read of the project's GitHub releases with a fixed
/// User-Agent and nothing about the machine, project or user; the version and the release page, no download. Release
/// texts are not read: only a tag that looks like a version and a page on this project's releases are taken.
/// </summary>
internal sealed partial class UpdateCheck(HttpMessageHandler handler, string currentVersion)
{
    public const string ReleasesApi = "https://api.github.com/repos/asckye/eplan-mcp/releases?per_page=10";

    public const string ReleasesPage = "https://github.com/asckye/eplan-mcp/releases/";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<UpdateResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout };
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EplanMcp-Workbench", currentVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Failed(string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)response.StatusCode}"));
            }

            using JsonDocument releases = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            (SemVersion Version, string Tag, string Page)? latest = null;
            foreach (JsonElement release in releases.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                string tag = release.TryGetProperty("tag_name", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : string.Empty;
                string page = release.TryGetProperty("html_url", out JsonElement u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : string.Empty;
                if (SemVersion.TryParse(tag, out SemVersion version) && page.StartsWith(ReleasesPage, StringComparison.Ordinal)
                    && (latest is null || version.CompareTo(latest.Value.Version) > 0))
                {
                    latest = (version, tag, page);
                }
            }

            if (latest is not { } found)
            {
                return Failed("no release");
            }

            bool newer = !SemVersion.TryParse(currentVersion, out SemVersion current) || found.Version.CompareTo(current) > 0;
            return new UpdateResult(newer ? UpdateState.Newer : UpdateState.Current, found.Tag, found.Page, string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return Failed(ex is TaskCanceledException ? "timeout" : ex.Message);
        }
    }

    private static UpdateResult Failed(string reason) => new(UpdateState.Failed, string.Empty, string.Empty, Approvals.ProjectText.Line(reason, 200));

    /// <summary>A version tag such as <c>v1.0.0</c> or <c>1.0.0-rc.1</c>; a pre-release sorts before its release.</summary>
    internal readonly partial record struct SemVersion(int Major, int Minor, int Patch, string Pre) : IComparable<SemVersion>
    {
        public static bool TryParse(string text, out SemVersion version)
        {
            Match match = Pattern().Match(text);
            version = default;
            if (!match.Success
                || !int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int major)
                || !int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
                || !int.TryParse(match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
            {
                return false;
            }

            version = new SemVersion(major, minor, patch, match.Groups[4].Value);
            return true;
        }

        public int CompareTo(SemVersion other)
        {
            int numbers = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
            if (numbers != 0)
            {
                return numbers;
            }

            if (Pre.Length == 0 || other.Pre.Length == 0)
            {
                return (Pre.Length == 0).CompareTo(other.Pre.Length == 0); // a release sorts after its pre-releases
            }

            return string.CompareOrdinal(Pre, other.Pre);
        }

        [GeneratedRegex(@"^v?(\d{1,5})\.(\d{1,5})\.(\d{1,5})(?:-([0-9A-Za-z.-]{1,40}))?$", RegexOptions.CultureInvariant)]
        private static partial Regex Pattern();
    }
}
