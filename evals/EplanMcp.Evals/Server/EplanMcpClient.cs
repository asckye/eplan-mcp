using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Evals.Server;

/// <summary>The result of one tool call: the structured result when it has one, and the text the model would read.</summary>
internal sealed record ToolOutcome(bool IsError, JsonObject? Structured, string Text);

/// <summary>What the runner asks eplanmcp itself, without a model: the preflight and the oracle calls (design 4.2).</summary>
internal interface IToolClient
{
    public Task<IReadOnlyList<string>> ListToolsAsync(CancellationToken cancellationToken);

    public Task<ToolOutcome> CallAsync(string tool, JsonObject args, CancellationToken cancellationToken);
}

/// <summary>An MCP client that starts <c>eplanmcp serve</c> over stdio, the same way Claude Code does.</summary>
internal sealed class EplanMcpClient : IToolClient, IAsyncDisposable
{
    private readonly McpClient _client;

    private EplanMcpClient(McpClient client) => _client = client;

    /// <summary><paramref name="environment"/> adds variables for eplanmcp, such as the test-only pipe prefix.</summary>
    public static async Task<EplanMcpClient> StartAsync(string eplanmcp, IDictionary<string, string?>? environment, CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "eplanmcp",
            Command = eplanmcp,
            Arguments = ["serve"],
            EnvironmentVariables = environment,
        });
        return new EplanMcpClient(await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<string>> ListToolsAsync(CancellationToken cancellationToken) =>
        [.. (await _client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Select(t => t.Name)];

    public async Task<ToolOutcome> CallAsync(string tool, JsonObject args, CancellationToken cancellationToken)
    {
        // JsonElement values reach the server exactly as the task file wrote them.
        Dictionary<string, object?> arguments = args.ToDictionary(a => a.Key, a => (object?)JsonSerializer.SerializeToElement(a.Value));
        CallToolResult result = await _client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new ToolOutcome(
            result.IsError == true,
            result.StructuredContent is { } structured ? JsonNode.Parse(structured.GetRawText()) as JsonObject : null,
            string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text)));
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
