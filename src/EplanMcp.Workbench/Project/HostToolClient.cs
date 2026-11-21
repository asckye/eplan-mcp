using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Workbench.Project;

/// <summary>What the project page reads from EPLAN: one read-only tool call, its structured result.</summary>
internal interface IProjectTools
{
    public Task<JsonObject> ReadAsync(string tool, JsonObject args, CancellationToken cancellationToken);
}

/// <summary>A tool call that ended in an error: the tool's own text, which is data from the Host, never instructions.</summary>
internal sealed class ProjectToolException(string tool, string message) : Exception(message)
{
    public string Tool { get; } = tool;
}

/// <summary>
/// The Workbench as a client of its own Host (ADR-0065 decision 1): <c>eplanmcp serve</c> over stdio, started on the
/// first call and ended with the Workbench, named <c>EplanMcp Workbench</c> in the Host's audit. Only the read-only
/// tools of <see cref="ReadTools"/> can be called here (decision 2); a write goes another way, with its own checks.
/// </summary>
internal sealed class HostToolClient(string hostExecutable) : IProjectTools, IAsyncDisposable
{
    /// <summary>The tools the project page reads with; every one is annotated read-only by the Host.</summary>
    public static readonly IReadOnlySet<string> ReadTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "eplan_session_status", "eplan_project_get", "eplan_page_list", "eplan_page_get", "eplan_device_list", "eplan_structure_list", "eplan_check_list",
    };

    private readonly SemaphoreSlim _start = new(1, 1);
    private McpClient? _client;
    private bool _disposed;

    public async Task<JsonObject> ReadAsync(string tool, JsonObject args, CancellationToken cancellationToken)
    {
        if (!ReadTools.Contains(tool))
        {
            throw new ArgumentException($"{tool} is not a read-only tool the Workbench may call.", nameof(tool));
        }

        McpClient client = await ClientAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = args.ToDictionary(a => a.Key, a => (object?)JsonSerializer.SerializeToElement(a.Value));
        CallToolResult result = await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        if (result.IsError == true)
        {
            throw new ProjectToolException(tool, text);
        }

        return result.StructuredContent is { } structured && JsonNode.Parse(structured.GetRawText()) is JsonObject json
            ? json
            : throw new ProjectToolException(tool, "The tool returned no structured result.");
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_client is { } client)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        _start.Dispose();
    }

    private async Task<McpClient> ClientAsync(CancellationToken cancellationToken)
    {
        await _start.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client is null)
            {
                var transport = new StdioClientTransport(new StdioClientTransportOptions { Name = "eplanmcp", Command = hostExecutable, Arguments = ["serve"] });
                string version = typeof(HostToolClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";
                var options = new McpClientOptions { ClientInfo = new Implementation { Name = "EplanMcp Workbench", Version = version } };
                _client = await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return _client;
        }
        finally
        {
            _start.Release();
        }
    }
}
