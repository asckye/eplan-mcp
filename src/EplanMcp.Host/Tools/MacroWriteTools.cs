using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
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

    private static readonly JsonSerializerOptions _itemJson = new(HostJson.Options)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

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
        "Inserts a window macro or a part's group symbol macro (give macro or part, MultiLine on Circuit pages, Overview on Overview pages), " +
        "its devices tagged and shown. One Ctrl+Z in EPLAN undoes insertion and value sets. macro_box:false removes boxes, true uses project settings. " +
        "x and y place the macro reference point in mm from the page's bottom left, y up. " +
        "values maps 1-20 exact, case-sensitive placeholder names to existing value sets from eplan_macro_get. " +
        "items inserts 1-20 macros across existing pages in one transaction. " +
        "show_page opens or focuses the editor before drawing, changing only the UI. " +
        "Omit dry_run to preview, false executes. Outside scratch it needs approval. Example: " +
        "{\"project\": \"Line1\", \"page\": \"=GAA+A1&EFS1/60\", \"macro\": \"EPLAN_Macro/Motor.ema\", \"x\": 40, \"y\": 280}.")]
    public static async Task<CallToolResult> MacroInsertAsync(
        WritePipeline pipeline,
        IBridgeClient bridge,
        string project,
        string? page = null,
        double? x = null,
        double? y = null,
        string? macro = null,
        int? variant = null,
        [AllowedValues("number", "keep")] string? numbering = null,
        bool dry_run = WriteToolDefaults.Preview,
        [Description("Open or focus page editor.")] bool show_page = false,
        [AllowedValues("MultiLine", "Overview", "SingleLine")] string? representation = null,
        string? part = null,
        string? part_variant = null,
        bool? macro_box = null,
        [AllowedValues("refuse", "warn")] string layout_guard = "refuse",
        Dictionary<string, string>? values = null,
        JsonElement? items = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The page, the macro, the point and the numbering are checked by the Bridge, all at once (macro.insert.plan).
        List<string> violations = ToolArguments.WriteProjectViolations(project);
        if (items is null)
        {
            foreach (var field in new (string Name, bool Missing)[] { ("page", page is null), ("x", x is null), ("y", y is null) })
            {
                if (field.Missing)
                {
                    violations.Add("Missing required argument '" + field.Name + "'.");
                }
            }
        }
        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new MacroInsertArgs
        {
            Project = project,
            Page = items is null ? page ?? string.Empty : page,
            Macro = macro,
            X = items is null ? x ?? 0 : x,
            Y = items is null ? y ?? 0 : y,
            Variant = items is null ? variant ?? 0 : variant,
            Numbering = items is null ? numbering ?? "number" : numbering,
            Representation = items is null ? representation ?? "MultiLine" : representation,
            Part = part,
            PartVariant = part_variant,
            MacroBox = items is null ? macro_box ?? true : macro_box,
            LayoutGuard = layout_guard,
            Values = values,
            Items = items?.Deserialize<List<MacroInsertItem>>(_itemJson),
        };
        var request = new WriteRequest<MacroInsertArgs, MacroInsertArgs>(
            InsertTool,
            ToolTier.Write,
            OperationNames.MacroInsertPlan,
            OperationNames.MacroInsertExecute,
            args,
            plan => new MacroInsertArgs
            {
                Project = args.Project,
                Page = args.Page,
                Macro = args.Macro,
                X = args.X,
                Y = args.Y,
                Variant = args.Variant,
                Numbering = args.Numbering,
                Representation = args.Representation,
                Part = args.Part,
                PartVariant = args.PartVariant,
                MacroBox = args.MacroBox,
                LayoutGuard = args.LayoutGuard,
                Values = args.Values,
                Items = args.Items,
                Fingerprint = plan.Fingerprint,
            },
            dry_run,
            items is null ? PageWriteTools.DefaultLimit : TimeSpan.FromSeconds(300),
            "Inserting the macro");
        PageDisplay display = new();
        if (show_page)
        {
            IEnumerable<string> displayPages = items is null ? [page ?? string.Empty] : args.Items!.Select(item => item.Page).Distinct(StringComparer.Ordinal);
            foreach (string displayPage in displayPages)
            {
                PageDisplay shown = await PageDisplay.ShowAsync(bridge, true, project, displayPage, cancellationToken).ConfigureAwait(false);
                string hint = string.Join(" ", new[] { display.Hint, shown.Hint }.OfType<string>());
                display = new PageDisplay(shown.PageShown is null ? null : (display.PageShown ?? true) && shown.PageShown.Value,
                    hint.Length == 0 ? null : hint);
            }
        }
        WriteOutcome<MacroInsertData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<MacroInsertArgs, MacroInsertArgs, MacroInsertData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return display.Apply(ToolResults.Error(ex.Error));
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        MacroInsertData? data = outcome.Data;
        var result = new MacroInsertResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Undo = committed ? "AI: " + InsertTool : null,
            Recovered = outcome.Recovered,
        };
        if (items is not null)
        {
            return BatchResult(result, outcome, details, display);
        }
        result.Page = ResultShaper.CleanText(data?.Page ?? outcome.Plan.Changes.FirstOrDefault()?.Name ?? args.Page);
        result.Macro = ResultShaper.CleanText(data?.Macro ?? details.GetValueOrDefault("macro") ?? string.Empty);
        result.ArticleReferences = data is null ? [] : [.. data.ArticleReferences.Select(r => new MacroPartReference { Tag = ResultShaper.CleanText(r.Tag), PartNumber = ResultShaper.CleanText(r.PartNumber), Variant = ResultShaper.CleanText(r.Variant), Count = r.Count })];
        result.Values = data is null ? [] : [.. data.Values.Select(v => new MacroValueApplication
        {
            Placeholder = ResultShaper.CleanText(v.Placeholder),
            ValueSet = ResultShaper.CleanText(v.ValueSet),
        })];
        result.Devices = data is null ? Planned(details) : [.. data.Devices.Select(d => Entry(d.Tag, d.VisibleTag, d.Category))];
        result.ObjectCount = data?.ObjectCount ?? Count(details, "objectCount");
        int devices = data?.DeviceCount ?? Count(details, "deviceCount");
        result.Hint = Hint(outcome, details, string.Equals(numbering, "keep", StringComparison.OrdinalIgnoreCase), result.Devices!.Count, devices);
        if (dry_run)
        { result.Hint = WriteToolDefaults.ExecutionHint + " " + result.Hint; }
        string what = "a macro with " + ToolResults.Count(devices, "device");
        return display.Apply(ToolResults.Success(
            committed ? $"Inserted {what}; Ctrl+Z in EPLAN undoes this step." : $"Dry run: would insert {what}. Nothing was changed.",
            result));
    }

    private static CallToolResult BatchResult(MacroInsertResult result, WriteOutcome<MacroInsertData> outcome,
        Dictionary<string, string> details, PageDisplay display)
    {
        List<MacroInsertData> data = outcome.Data?.Items
            ?? JsonSerializer.Deserialize<List<MacroInsertData>>(details.GetValueOrDefault("items") ?? "[]", HostJson.Options)!;
        result.ItemCount = data.Count;
        result.DeviceCount = data.Sum(item => item.DeviceCount);
        result.Items = [.. data.Select(item => new MacroInsertResult.ItemEntry
        {
            Page = ResultShaper.CleanText(item.Page),
            Macro = ResultShaper.CleanText(item.Macro),
            DeviceCount = item.DeviceCount,
            ObjectCount = item.ObjectCount,
            ValueCount = outcome.Execution == ExecutionState.Committed ? item.ValueCount ?? 0 : 0,
        })];
        result.Hint = string.Join(" ", new[]
        {
            result.DryRun ? WriteToolDefaults.ExecutionHint : null,
            outcome.Recovered ? "EPLAN reported the batch committed after the response was lost." : null,
            "Use eplan_page_get for devices and eplan_placeholder_list for applied placeholder values.",
            ResultShaper.CleanText(outcome.Data?.Warning ?? details.GetValueOrDefault("layoutWarnings")),
        }.OfType<string>());
        string count = ToolResults.Count(data.Count, "macro") + " with " + ToolResults.Count(result.DeviceCount ?? 0, "device");
        return display.Apply(ToolResults.Success(outcome.Execution == ExecutionState.Committed
            ? $"Inserted {count}; Ctrl+Z in EPLAN undoes this step."
            : $"Dry run: would insert {count}. Nothing was changed.", result));
    }

    internal static List<string> BatchViolations(JsonElement schema, IDictionary<string, JsonElement> arguments)
    {
        JsonElement items = arguments["items"];
        List<string> violations = UnknownArgumentFilter.Violations(schema, arguments, jsonPointers: true);
        JsonElement fields = schema.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("properties");
        foreach (JsonProperty field in fields.EnumerateObject().Where(field => arguments.ContainsKey(field.Name)))
        {
            violations.Add("/" + field.Name + ": Omit this top-level field when 'items' is supplied.");
        }
        if (items.ValueKind != JsonValueKind.Array)
        {
            violations.Add("/items: Supply an array of 1 to 20 insertion objects.");
        }
        else if (items.GetArrayLength() is < 1 or > 20)
        {
            violations.Add("/items: Supply between 1 and 20 insertions.");
        }
        return violations;
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

        if (ResultShaper.CleanText(data?.Warning ?? details.GetValueOrDefault("layoutWarnings")) is { Length: > 0 } warning)
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
