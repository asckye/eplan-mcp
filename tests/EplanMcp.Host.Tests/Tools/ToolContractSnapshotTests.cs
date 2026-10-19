using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// The tool contract (mcp-tool-design 11) as a reviewed snapshot: the server instructions and every tool exactly as
/// <c>tools/list</c> sends it, on the tools as <c>serve</c> registers them. Any change fails this test until the new
/// snapshot in tests/Contracts.Snapshots is reviewed and committed (testing.md 3). Medium: the snapshot is a file.
/// </summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class ToolContractSnapshotTests
{
    // The SDK's wire format, laid out for review: indented, LF line ends, '+' and non-ASCII text left unescaped.
    private static readonly JsonSerializerOptions _snapshotJson = new(McpJsonUtilities.DefaultOptions)
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task Contract_MatchesTheReviewedSnapshot()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddEplanMcp();
        await using ServiceProvider provider = services.BuildServiceProvider();
        McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        // Sorted by name, so the snapshot does not depend on registration order.
        var contract = new JsonObject
        {
            ["instructions"] = options.ServerInstructions,
            ["tools"] = new JsonArray([.. options.ToolCollection!
                .Select(t => t.ProtocolTool)
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => JsonSerializer.SerializeToNode(t, _snapshotJson))]),
        };

        await Verify(contract.ToJsonString(_snapshotJson) + "\n", "json")
            .UseDirectory("../../Contracts.Snapshots")
            .UseFileName("server-contract");
    }
}
