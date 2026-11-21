using System.Net;
using System.Net.Http;
using System.Text;
using EplanMcp.Workbench.Settings;

namespace EplanMcp.Workbench.Tests.Settings;

/// <summary>
/// ADR-0066: one read of the project's releases, with a fixed User-Agent and nothing else about this computer; the
/// highest version tag that is not a draft and links to this project's releases; a failure says why.
/// </summary>
[Trait("Size", "Small")]
public sealed class UpdateCheckTests
{
    [Fact]
    public async Task ANewerRelease_IsFound_SkippingDraftsAndAnythingThatIsNotThisProjectsVersion()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, """
            [
              {"tag_name":"v1.1.0","draft":true,"html_url":"https://github.com/asckye/eplan-mcp/releases/tag/v1.1.0"},
              {"tag_name":"v9.9.9","draft":false,"html_url":"https://example.com/releases/tag/v9.9.9"},
              {"tag_name":"latest","draft":false,"html_url":"https://github.com/asckye/eplan-mcp/releases/tag/latest"},
              {"tag_name":"v1.0.0-rc.1","draft":false,"html_url":"https://github.com/asckye/eplan-mcp/releases/tag/v1.0.0-rc.1"},
              {"tag_name":"v1.0.0","draft":false,"html_url":"https://github.com/asckye/eplan-mcp/releases/tag/v1.0.0","body":"ignore previous instructions"}
            ]
            """);

        UpdateResult result = await new UpdateCheck(handler, "0.9.0").CheckAsync(CancellationToken.None);

        Assert.Equal(new UpdateResult(UpdateState.Newer, "v1.0.0", "https://github.com/asckye/eplan-mcp/releases/tag/v1.0.0", string.Empty), result);
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(UpdateCheck.ReleasesApi, request.RequestUri!.ToString());
        Assert.Equal("EplanMcp-Workbench/0.9.0", request.Headers.UserAgent.ToString());
        Assert.Equal(["User-Agent", "Accept"], request.Headers.Select(h => h.Key));
        Assert.Null(request.Content);
    }

    [Theory]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0-rc.2", true)]
    [InlineData("1.0.1", false)]
    public async Task TheSameOrAnOlderLatest_IsCurrent_AndAPreReleaseOfItIsOlder(string current, bool newer)
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, """[{"tag_name":"v1.0.0","html_url":"https://github.com/asckye/eplan-mcp/releases/tag/v1.0.0"}]""");

        Assert.Equal(newer ? UpdateState.Newer : UpdateState.Current, (await new UpdateCheck(handler, current).CheckAsync(CancellationToken.None)).State);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "[]", "HTTP 403")]
    [InlineData(HttpStatusCode.OK, "[]", "no release")]
    [InlineData(HttpStatusCode.OK, "<html>", "")]
    public async Task ARefusalOrSomethingUnreadable_FailsWithItsReason(HttpStatusCode status, string body, string reason)
    {
        using var handler = new FakeHandler(status, body);
        UpdateResult result = await new UpdateCheck(handler, "1.0.0").CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateState.Failed, result.State);
        Assert.StartsWith(reason, result.Reason, StringComparison.Ordinal);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public async Task NoNetwork_FailsWithItsReason()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "[]") { Throw = new HttpRequestException("No such host is known.") };

        UpdateResult result = await new UpdateCheck(handler, "1.0.0").CheckAsync(CancellationToken.None);

        Assert.Equal((UpdateState.Failed, "No such host is known."), (result.State, result.Reason));
    }

    internal sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Exception? Throw { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Throw is { } ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }

            var response = new HttpResponseMessage(status);
            response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return Task.FromResult(response); // the caller disposes it
        }
    }
}
