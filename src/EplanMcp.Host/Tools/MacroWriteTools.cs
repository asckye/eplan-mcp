using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The macro write tool (docs/tools/eplan_macro_insert.md), through <see cref="WritePipeline"/> like every write. The
/// Bridge checks the page, the macro's path (SEC-19) and where the outline goes; the Host shapes the result.
/// </summary>
[McpServerToolType]
internal sealed class MacroWriteTools
{
    public const string InsertTool = "eplan_macro_insert";

    [McpServerTool(
        Name = InsertTool,
        Title = "Insert a window macro on an EPLAN page",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(MacroInsertResult))]
    [Description(
        "Inserts a window macro on a circuit page, its devices tagged and shown; one Ctrl+Z in EPLAN undoes it. x, y: mm " +
        "from the page's bottom left (y up) to the macro's top left corner. Outside scratch it needs approval. Example: " +
        "{\"project\": \"Line1\", \"page\": \"=GAA+A1&EFS1/60\", \"macro\": \"EPLAN_Macro/Motor.ema\", \"x\": 40, \"y\": 280}.")]
    public static async Task<CallToolResult> MacroInsertAsync(
        WritePipeline pipeline,
        [Description("From eplan_project_list")] string project,
        [Description("Full circuit page name")] string page,
        [Description("Path from eplan_macro_list")] string macro,
        [Description("mm")] double x,
        [Description("mm")] double y,
        [Description("From eplan_macro_get")] int variant = 0,
        [Description("keep: the macro's tags")][AllowedValues("number", "keep")] string numbering = "number",
        [Description("Only return the plan")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The page, the macro, the point and the numbering are checked by the Bridge, all at once (macro.insert.plan).
        if (ToolArguments.WriteProjectViolations(project) is { Count: > 0 } violations)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new MacroInsertArgs { Project = project, Page = page ?? string.Empty, Macro = macro, X = x, Y = y, Variant = variant, Numbering = numbering };
        var request = new WriteRequest<MacroInsertArgs, MacroInsertArgs>(
            InsertTool,
            ToolTier.Write,
            OperationNames.MacroInsertPlan,
            OperationNames.MacroInsertExecute,
            args,
            plan => new MacroInsertArgs { Project = args.Project, Page = args.Page, Macro = args.Macro, X = args.X, Y = args.Y, Variant = args.Variant, Numbering = args.Numbering, Fingerprint = plan.Fingerprint },
            dry_run,
            PageWriteTools.DefaultLimit,
            "Inserting the macro");
        WriteOutcome<MacroInsertData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<MacroInsertArgs, MacroInsertArgs, MacroInsertData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        MacroInsertData? data = outcome.Data;
        var result = new MacroInsertResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Page = ResultShaper.CleanText(data?.Page ?? outcome.Plan.Changes.FirstOrDefault()?.Name ?? args.Page),
            Macro = ResultShaper.CleanText(data?.Macro ?? details.GetValueOrDefault("macro") ?? string.Empty),
            Devices = data is null ? Planned(details) : [.. data.Devices.Select(d => Entry(d.Tag, d.VisibleTag, d.Category))],
            ObjectCount = data?.ObjectCount ?? Count(details, "objectCount"),
            Undo = committed ? "AI: " + InsertTool : null,
            Recovered = outcome.Recovered,
        };
        int devices = data?.DeviceCount ?? Count(details, "deviceCount");
        result.Hint = Hint(outcome, details, string.Equals(numbering, "keep", StringComparison.OrdinalIgnoreCase), result.Devices.Count, devices);
        string what = "a macro with " + ToolResults.Count(devices, "device");
        return ToolResults.Success(
            committed ? $"Inserted {what}; Ctrl+Z in EPLAN undoes this step." : $"Dry run: would insert {what}. Nothing was changed.",
            result);
    }

    /// <summary>The hints of the tool spec (section 3), each when it applies, in one text.</summary>
    private static string? Hint(WriteOutcome<MacroInsertData> outcome, Dictionary<string, string> details, bool keep, int listed, int devices)
    {
        MacroInsertData? data = outcome.Data;
        var hints = new List<string>();
        if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the macro as inserted; 'devices' are the macro's own, so check the page with eplan_page_get.");
        }
        else if (data is null)
        {
            hints.Add(keep
                ? "The tags are the macro's placeholders: inserted with numbering keep, the devices keep them in the page's structure, and the insert is refused if that merges them with existing devices."
                : "The tags are the macro's placeholders: inserted with numbering number, EPLAN gives the devices tags no device has, in the page's structure.");
        }
        else if (data.Devices.Any(d => d.Tag.Contains('?', StringComparison.Ordinal)))
        {
            hints.Add("Tags with '?' could not be numbered (a contact without its coil, a terminal without a number): check them in EPLAN.");
        }

        if (!(data?.PageSizeKnown ?? details.GetValueOrDefault("pageSizeKnown") == "true"))
        {
            hints.Add("EPLAN cannot tell this page's size (a new, empty page), so only x >= 0 and y >= 0 were checked: check in EPLAN that the macro lies within the page's frame.");
        }

        if (listed < devices)
        {
            hints.Add($"Only the first {listed.ToString(CultureInfo.InvariantCulture)} of {devices.ToString(CultureInfo.InvariantCulture)} devices are listed.");
        }

        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        return hints.Count == 0 ? null : string.Join(" ", hints);
    }

    /// <summary>The macro's devices from the plan's details (macro.insert.plan): one per line, tag, shown tag and category.</summary>
    private static List<MacroInsertResult.DeviceEntry> Planned(Dictionary<string, string> details) =>
        details.GetValueOrDefault("devices") is { Length: > 0 } lines
            ? [.. lines.Split('\n').Select(line => line.Split('\t')).Select(p => Entry(p[0], p.Length > 1 && p[1].Length > 0 ? p[1] : null, p.Length > 2 ? p[2] : string.Empty))]
            : [];

    private static int Count(Dictionary<string, string> details, string key) =>
        int.TryParse(details.GetValueOrDefault(key), NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : 0;

    private static MacroInsertResult.DeviceEntry Entry(string tag, string? visibleTag, string category) => new()
    {
        Tag = ResultShaper.CleanText(tag),
        VisibleTag = ResultShaper.CleanText(visibleTag),
        Category = ResultShaper.CleanText(category),
    };
}
