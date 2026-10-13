using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Spikes.S4;

/// <summary>
/// Two spike tools. <c>s4_probe</c> reports what the client negotiated and declared. <c>s4_approve</c> plays the
/// Host's approval step for a fake change list: it raises a form elicitation with <see cref="InputRequiredException"/>
/// (the API that serves both protocol eras) and reports exactly what came back on the retry. Nothing is modified.
/// </summary>
[McpServerToolType]
public sealed class S4Tools
{
    private const string ApproveTool = "s4_approve";

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

    [McpServerTool(Name = ApproveTool, Title = "Spike S4: approval round-trip", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Spike S4 diagnostic. Asks the user to approve a SIMULATED EPLAN change list through an MCP elicitation form and reports the answer (accept/decline/cancel) and how the request state round-tripped. Changes nothing; no EPLAN project is touched.")]
    public static string Approve(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        [Description("Form variant to render: 'boolean' (one checkbox) or 'enum' (approve/reject choice).")] string form = "boolean",
        [Description("How many consecutive approval forms to ask for (1-3), to see whether repeated input requests work.")] int rounds = 1)
    {
        CallToolRequestParams parameters = context.Params!;
        string digest = RequestStateCodec.Digest(ApproveTool, parameters.Arguments);
        rounds = Math.Clamp(rounds, 1, 3);

        IDictionary<string, InputResponse>? responses = parameters.InputResponses;
        int answered = responses is null ? 0 : Enumerable.Range(1, 3).LastOrDefault(k => responses.ContainsKey(Key(k)));
        (string stateStatus, JsonObject? statePayload) = RequestStateCodec.Verify(parameters.RequestState, digest);

        var report = new JsonObject
        {
            ["negotiatedProtocolVersion"] = server.NegotiatedProtocolVersion,
            ["isMrtrSupported"] = server.IsMrtrSupported,
            ["elicitationDeclared"] = ToNode(server.ClientCapabilities?.Elicitation),
            ["form"] = form,
            ["roundsRequested"] = rounds,
            ["roundsAnswered"] = answered,
            ["requestState"] = new JsonObject
            {
                ["present"] = parameters.RequestState is not null,
                ["status"] = stateStatus,
                ["secondsSinceIssued"] = statePayload?["iat"] is { } iat ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - iat.GetValue<long>() : null,
            },
            ["inputResponsesRaw"] = responses is null ? null : JsonSerializer.SerializeToNode(responses, McpJsonUtilities.DefaultOptions),
        };

        if (answered > 0)
        {
            ElicitResult? last = responses![Key(answered)].Deserialize(InputResponse.ElicitResultJsonTypeInfo);
            report["lastAnswer"] = ToNode(last);
            bool approved = last is { Action: "accept" } && IsApproval(last.Content, form);
            if (!approved || answered >= rounds)
            {
                report["outcome"] = approved ? "APPROVED (simulated; nothing executed)" : "NOT APPROVED (" + (last?.Action ?? "no answer") + ")";
                return report.ToJsonString(_indented);
            }
        }

        if (!server.IsMrtrSupported || server.ClientCapabilities?.Elicitation is null)
        {
            // The spec forbids sending input requests the client did not declare; production falls back to the EPLAN dialog.
            report["outcome"] = "FALLBACK: client cannot be asked (no MRTR or no elicitation capability)";
            return report.ToJsonString(_indented);
        }

        int next = answered + 1;
        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest> { [Key(next)] = InputRequest.ForElicitation(ApprovalForm(form, next, rounds)) },
            requestState: RequestStateCodec.Issue(digest, next));
    }

    private static string Key(int round) => "approval" + round.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What a real approval form will carry (mcp-tool-design 8): tool, project, the change list, whether it can be undone.</summary>
    private static ElicitRequestParams ApprovalForm(string form, int round, int rounds)
    {
        var message = new StringBuilder()
            .Append("【Spike S4 模拟，不会修改任何项目】").Append(rounds > 1 ? $" 第 {round}/{rounds} 次确认" : string.Empty).Append('\n')
            .Append("工具 eplan_device_place 请求修改项目 'S4_Demo'（不在 scratch 中）：\n")
            .Append("  1. 新建页面 =AP+ST1/12\n")
            .Append("  2. 在 =AP+ST1/12 的 (100, 160) mm 放置 -K1（接触器）\n")
            .Append("  3. 设置 -K1 的功能文本为“主接触器”\n")
            .Append("执行后可在 EPLAN 中按一次 Ctrl+Z 全部撤销。审批 5 分钟内有效。");

        ElicitRequestParams.PrimitiveSchemaDefinition field = string.Equals(form, "enum", StringComparison.Ordinal)
            ? new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = "决定",
                Description = "批准或拒绝以上修改",
                OneOf =
                [
                    new ElicitRequestParams.EnumSchemaOption { Const = "approve", Title = "批准执行这 3 处修改" },
                    new ElicitRequestParams.EnumSchemaOption { Const = "reject", Title = "拒绝，不修改项目" },
                ],
            }
            : new ElicitRequestParams.BooleanSchema
            {
                Title = "批准这 3 处修改",
                Description = "勾选表示批准；不勾选或拒绝都不会修改项目",
                Default = false,
            };

        string fieldName = string.Equals(form, "enum", StringComparison.Ordinal) ? "decision" : "approve";
        return new ElicitRequestParams
        {
            Mode = "form",
            Message = message.ToString(),
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { [fieldName] = field },
                Required = [fieldName],
            },
        };
    }

    private static bool IsApproval(IDictionary<string, JsonElement>? content, string form)
    {
        if (content is null)
        {
            return false;
        }

        return string.Equals(form, "enum", StringComparison.Ordinal)
            ? content.TryGetValue("decision", out JsonElement d) && d.ValueKind == JsonValueKind.String && d.GetString() == "approve"
            : content.TryGetValue("approve", out JsonElement a) && a.ValueKind == JsonValueKind.True;
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
