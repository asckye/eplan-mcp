using System.ComponentModel;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Addin;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Starting EPLAN from the Host (ADR-0058, docs/tools/eplan_session_start.md). Closing and restarting follow in their
/// own change. Parameter names are snake_case because the SDK publishes them as written.
/// </summary>
[McpServerToolType]
internal sealed class SessionLifecycleTools
{
    public const string StartTool = "eplan_session_start";

    [McpServerTool(Name = StartTool, Title = "Start EPLAN with the EplanMcp add-in", ReadOnly = false, Destructive = false,
        Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SessionStartResult))]
    [Description(
        "Starts EPLAN on this computer and waits up to 3 minutes for its EplanMcp add-in to connect; use it when " +
        "eplan_session_status reports no EPLAN. Refuses while any EPLAN runs. Opens no project: then call " +
        "eplan_project_open. Takes an EPLAN licence and shows EPLAN's window. Example: {\"variant\": \"Electric P8\"}.")]
    public static async Task<CallToolResult> StartAsync(
        EplanLifecycle lifecycle,
        IProgress<ProgressNotificationValue> progress,
        [Description("Product variant, e.g. Electric P8 or Pro Panel; default Electric P8")] string? variant = null,
        CancellationToken cancellationToken = default)
    {
        int step = 0;
        EplanStart started;
        try
        {
            started = await lifecycle.StartAsync(variant, message => progress.Report(new ProgressNotificationValue { Progress = ++step, Message = message }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (EplanLifecycleException ex)
        {
            BridgeError error = ex.Reason == ErrorReason.InvalidArgument
                ? ToolResults.InvalidArgumentError([ex.Message])
                : new BridgeError { Reason = ex.Reason, Message = ex.Message, Retryable = false, Metadata = new Dictionary<string, string>(ex.Metadata) };
            return ToolResults.Error(error);
        }

        var result = new SessionStartResult
        {
            ProcessId = started.ProcessId,
            Variant = started.Variant,
            Version = started.Version,
            BridgeVersion = started.Handshake.BridgeVersion,
            Seconds = started.Seconds,
            Hint = "No project is open; call eplan_project_open, or eplan_project_list for what EPLAN opened by itself.",
        };
        return ToolResults.Success(string.Create(CultureInfo.InvariantCulture,
            $"Started EPLAN {result.Version} ({result.Variant}), process {result.ProcessId}, connected after {result.Seconds:0} s."), result);
    }
}

/// <summary>Output of <c>eplan_session_start</c>.</summary>
internal sealed class SessionStartResult
{
    [Description("The Windows process ID of the EPLAN started, as eplan_session_select takes it.")]
    public int ProcessId { get; set; }

    [Description("The product variant started.")]
    public string Variant { get; set; } = string.Empty;

    [Description("EPLAN's file version.")]
    public string Version { get; set; } = string.Empty;

    [Description("The EplanMcp add-in version that answered.")]
    public string BridgeVersion { get; set; } = string.Empty;

    [Description("Seconds from the start to the add-in's handshake.")]
    public double Seconds { get; set; }

    [Description("What to do next.")]
    public string? Hint { get; set; }
}
