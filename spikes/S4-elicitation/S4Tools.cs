using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Spikes.S4;

/// <summary>
/// Spike tools. <c>s4_probe</c> reports what the client negotiated and declared. Nothing is modified.
/// </summary>
[McpServerToolType]
public sealed class S4Tools
{
    [McpServerTool(Name = "s4_probe", Title = "Spike S4: client probe", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Spike S4 diagnostic. Reports the MCP protocol revision negotiated with this client, the client info and the capabilities it declared (e.g. elicitation). Changes nothing.")]
    public static string Probe(McpServer server, RequestContext<CallToolRequestParams> context)
    {
        var report = new JsonObject
        {
            ["negotiatedProtocolVersion"] = server.NegotiatedProtocolVersion,
            ["isMrtrSupported"] = server.IsMrtrSupported,
            ["clientInfo"] = ToNode(server.ClientInfo),
            ["clientCapabilities"] = ToNode(server.ClientCapabilities),
            ["requestMetaKeys"] = new JsonArray([.. (context.Params?.Meta?.Select(p => (JsonNode?)JsonValue.Create(p.Key)) ?? [])]),
            ["process"] = new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                // S5 / ADR-0008: a process started by an MSIX-packaged client may itself carry package identity.
                ["packageFullName"] = PackageFullName(),
                ["localAppData"] = Environment.GetEnvironmentVariable("LOCALAPPDATA"),
            },
        };
        return report.ToJsonString(_indented);
    }

    private static JsonNode? ToNode<T>(T value) => value is null ? null : JsonSerializer.SerializeToNode(value, McpJsonUtilities.DefaultOptions);

    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string? PackageFullName()
    {
        int length = 0;
        int rc = GetCurrentPackageFullName(ref length, null);
        if (rc == 15700)
        {
            return null; // APPMODEL_ERROR_NO_PACKAGE: no package identity
        }

        var buffer = new char[length];
        rc = GetCurrentPackageFullName(ref length, buffer);
        return rc == 0 ? new string(buffer, 0, Math.Max(0, length - 1)) : "error " + rc.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, [Out] char[]? packageFullName);
}
