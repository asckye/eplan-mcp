using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>The Host-side errors about reaching EPLAN. Messages tell the model what to do next (mcp-tool-design 6).</summary>
internal static class BridgeErrors
{
    public static BridgeError NotConnected() => new()
    {
        Reason = ErrorReason.EplanNotConnected,
        Message = "No running EPLAN with the EplanMcp add-in was found. Ask the engineer to start EPLAN and make sure the EplanMcp add-in is loaded, then retry. eplan_session_status shows details.",
        Retryable = true,
    };

    public static BridgeError ConnectionLost() => new()
    {
        Reason = ErrorReason.EplanNotConnected,
        Message = "The connection to EPLAN was lost; EPLAN may have been closed. Retry; if it keeps failing, check eplan_session_status.",
        Retryable = true,
    };

    public static BridgeError SessionNotSelected(IReadOnlyList<BridgeEndpoint> instances) => new()
    {
        Reason = ErrorReason.SessionNotSelected,
        Message = $"{instances.Count.ToString(CultureInfo.InvariantCulture)} EPLAN instances with the EplanMcp add-in are running. " +
            "Call eplan_session_status to see them, then eplan_session_select with the processId of the one to work with.",
        Metadata = new Dictionary<string, string> { ["processIds"] = ProcessIds(instances) },
    };

    /// <summary>eplan_session_select was given a process in which no Bridge runs (tool spec eplan_session_select 4).</summary>
    public static BridgeError UnknownInstance(int processId, IReadOnlyList<BridgeEndpoint> instances)
    {
        string violation = $"'process_id' {processId.ToString(CultureInfo.InvariantCulture)} is not an EPLAN with the EplanMcp add-in; " +
            "use one of metadata.processIds, as eplan_session_status lists them.";
        return new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "Invalid arguments: " + violation,
            Metadata = new Dictionary<string, string> { ["violations"] = violation, ["processIds"] = ProcessIds(instances) },
        };
    }

    public static BridgeError Incompatible(int bridgeVersion) => new()
    {
        Reason = ErrorReason.BridgeIncompatible,
        Message = $"The EplanMcp add-in in EPLAN speaks protocol {bridgeVersion.ToString(CultureInfo.InvariantCulture)}, but this server needs " +
            $"{BridgeProtocol.MinimumSupportedVersion.ToString(CultureInfo.InvariantCulture)}-{BridgeProtocol.CurrentVersion.ToString(CultureInfo.InvariantCulture)}. " +
            "Ask the engineer to run 'eplanmcp install-addin' and restart EPLAN.",
    };

    /// <summary>The add-in is older than this Host: it handshakes fine but lacks the operation a tool needs.</summary>
    public static BridgeError OperationMissing(string operation, string bridgeVersion) => new()
    {
        Reason = ErrorReason.BridgeIncompatible,
        Message = $"The EplanMcp add-in in EPLAN (version {bridgeVersion}) is older than this server and cannot do '{operation}'. " +
            "Ask the engineer to run 'eplanmcp install-addin' and restart EPLAN.",
        Metadata = new Dictionary<string, string> { ["operation"] = operation, ["bridgeVersion"] = bridgeVersion },
    };

    public static BridgeError QueueBusy() => new()
    {
        Reason = ErrorReason.EplanBusy,
        Message = "Another EplanMcp call is still waiting for EPLAN. Retry in a moment.",
        Retryable = true,
    };

    public static BridgeError Timeout(TimeSpan limit) => new()
    {
        Reason = ErrorReason.Timeout,
        Message = $"EPLAN did not answer within {limit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s. Retry, or ask the engineer whether EPLAN is busy.",
        Retryable = true,
    };

    public static BridgeError Internal(string correlationId) => new()
    {
        Reason = ErrorReason.Internal,
        Message = $"An internal error occurred in EplanMcp (correlation ID {correlationId}). Please report it.",
        Metadata = new Dictionary<string, string> { ["correlationId"] = correlationId },
    };

    private static string ProcessIds(IReadOnlyList<BridgeEndpoint> instances) =>
        string.Join(",", instances.Select(i => i.ProcessId.ToString(CultureInfo.InvariantCulture)));
}
