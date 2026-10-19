using System.ComponentModel;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class SessionTools
{
    public const string StatusTool = "eplan_session_status";
    public const string SelectTool = "eplan_session_select";

    [McpServerTool(
        Name = StatusTool,
        Title = "EPLAN session status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SessionStatusResult))]
    [Description(
        "Reports whether EPLAN is running with the EplanMcp add-in, and which EPLAN version and variant this server is connected to. " +
        "Use it first, or when another tool returns EPLAN_NOT_CONNECTED or BRIDGE_INCOMPATIBLE: it succeeds even when EPLAN is not available " +
        "and says in 'hint' what the engineer should do. Does not touch any project and does not wait for EPLAN's main thread. " +
        "Related: eplan_project_list.")]
    public static async Task<CallToolResult> SessionStatusAsync(IBridgeClient bridge, CancellationToken cancellationToken)
    {
        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        HandshakeResult? handshake = status.Handshake;
        var result = new SessionStatusResult
        {
            Connected = handshake is not null,
            Host = new SessionStatusResult.HostSummary { Version = HostInfo.Version, ProtocolVersion = BridgeProtocol.CurrentVersion },
            Eplan = handshake is null ? null : new SessionStatusResult.EplanSummary
            {
                ProcessId = handshake.Eplan.ProcessId,
                Version = handshake.Eplan.Version,
                Variant = handshake.Eplan.Variant,
                BridgeVersion = handshake.BridgeVersion,
                ProtocolVersion = handshake.ProtocolVersion,
                ApiExtensionLicence = handshake.Eplan.ApiExtensionLicence,
            },
            Instances =
            [
                .. status.Instances.Select(i => new SessionStatusResult.InstanceSummary { ProcessId = i.ProcessId, Selected = i.ProcessId == status.SelectedProcessId }),
            ],
            Hint = status.Problem?.Message,
        };
        return ToolResults.Success(Summary(result, status.Problem), result);
    }

    [McpServerTool(
        Name = SelectTool,
        Title = "Choose the EPLAN to work with",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SessionSelectResult))]
    [Description(
        "Chooses which running EPLAN this server works with when several have the add-in loaded and other tools return " +
        "SESSION_NOT_SELECTED. Take process_id from the instances of eplan_session_status; the result names that EPLAN's " +
        "version and variant, so select another if it is not the one meant. Lasts until that EPLAN exits; changes nothing " +
        "in EPLAN. Example: {\"process_id\": 24688}.")]
    public static async Task<CallToolResult> SessionSelectAsync(
        IBridgeClient bridge,
        [Description("EPLAN process ID from eplan_session_status")] int process_id,
        CancellationToken cancellationToken = default)
    {
        if (process_id <= 0)
        {
            return ToolResults.InvalidArguments(["'process_id' must be the processId of an EPLAN that eplan_session_status lists."]);
        }

        HandshakeResult handshake;
        try
        {
            handshake = await bridge.SelectAsync(process_id, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        var result = new SessionSelectResult
        {
            Selected = new SessionSelectResult.SelectedEplan
            {
                ProcessId = handshake.Eplan.ProcessId,
                Version = handshake.Eplan.Version,
                Variant = handshake.Eplan.Variant,
            },
        };
        string variant = result.Selected.Variant is { Length: > 0 } v ? $" ({v})" : string.Empty;
        return ToolResults.Success($"Selected EPLAN {result.Selected.Version}{variant}, process {result.Selected.ProcessId.ToString(CultureInfo.InvariantCulture)}.", result);
    }

    private static string Summary(SessionStatusResult result, BridgeError? problem)
    {
        if (result.Eplan is { } eplan)
        {
            string variant = eplan.Variant is { Length: > 0 } v ? $" ({v})" : string.Empty;
            return $"EPLAN {eplan.Version}{variant} connected, process {eplan.ProcessId.ToString(CultureInfo.InvariantCulture)}.";
        }

        return $"EPLAN not connected: {problem?.Reason ?? ErrorReason.EplanNotConnected}. See 'hint'.";
    }
}
