using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The device write tools (docs/tools/eplan_device_update.md, eplan_device_place.md), through <see cref="WritePipeline"/>
/// like every write: rename a device, and place one by part number.
/// </summary>
[McpServerToolType]
internal sealed class DeviceWriteTools
{
    public const string UpdateTool = "eplan_device_update";

    public const string PlaceTool = "eplan_device_place";

    [McpServerTool(
        Name = UpdateTool,
        Title = "Update a device in an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DeviceUpdateResult))]
    [Description("Renames a device preserving suffixes; terminal-only PLC modules may join an existing PLC box. Append a part to its unique main function, or one exact terminal without renaming. Give new_tag, part, or both. One Ctrl+Z undoes it. Omit/true dry_run previews; false executes. Outside scratch needs approval. See usage.")]
    public static async Task<CallToolResult> DeviceUpdateAsync(
        WritePipeline pipeline,
        [Description("Open project")] string project,
        [Description("Current tag, full or as shown, e.g. -KF1")] string tag,
        [Description("Full new tag, or from '-' to keep the structure")] string? new_tag = null,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        [Description("Part for one main function or exact terminal")] string? part = null,
        [Description("Part variant; requires part, defaults to 1")] string? variant = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The tags are checked by the Bridge, all at once (device.update.plan).
        if (ToolArguments.WriteProjectViolations(project) is { Count: > 0 } violations)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new DeviceUpdateArgs { Project = project, Tag = tag ?? string.Empty, NewTag = new_tag ?? string.Empty, Part = part, Variant = variant };
        var request = new WriteRequest<DeviceUpdateArgs, DeviceUpdateArgs>(
            UpdateTool,
            ToolTier.Write,
            OperationNames.DeviceUpdatePlan,
            OperationNames.DeviceUpdateExecute,
            args,
            plan => new DeviceUpdateArgs { Project = args.Project, Tag = args.Tag, NewTag = args.NewTag, Part = args.Part, Variant = args.Variant, Fingerprint = plan.Fingerprint },
            dry_run,
            PageWriteTools.DefaultLimit,
            "Updating the device");
        WriteOutcome<DeviceUpdateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<DeviceUpdateArgs, DeviceUpdateArgs, DeviceUpdateData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        var result = new DeviceUpdateResult
        {
            Project = ResultShaper.CleanText(outcome.Data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. outcome.Plan.Changes.Select(c => new PlannedChange { Action = c.Action, Kind = c.Kind, Name = ResultShaper.CleanText(c.Name), Property = ResultShaper.CleanText(c.Property), OldValue = ResultShaper.CleanText(c.OldValue), NewValue = ResultShaper.CleanText(c.NewValue) })],
            Device = outcome.Data is { } data ? Device(data) : Planned(outcome.Plan),
            Undo = committed ? "AI: " + UpdateTool : null,
            Recovered = outcome.Recovered,
            Hint = dry_run ? WriteToolDefaults.ExecutionHint : outcome.Recovered
                ? "The response was lost, but EPLAN reported the update as committed; check the device with eplan_device_get."
                : ResultShaper.CleanText(outcome.Data?.Warning),
        };
        string what = $"a device and its {ToolResults.Count(Math.Max(0, result.Device.Functions - 1), "auxiliary function")}";
        return ToolResults.Success(
            part is not null ? committed ? "Updated one device and appended one part reference; Ctrl+Z in EPLAN undoes this step." : "Dry run: would update one device and append one part reference. Nothing was changed."
                : committed ? $"Renamed {what}; Ctrl+Z in EPLAN undoes this step." : $"Dry run: would rename {what}. Nothing was changed.",
            result);
    }

    internal static Task<CallToolResult> DeviceUpdateAsync(WritePipeline pipeline, string project, string tag, string? newTag,
        bool dryRun, IProgress<ProgressNotificationValue>? legacyProgress, CancellationToken cancellationToken) =>
        DeviceUpdateAsync(pipeline, project, tag, newTag, dryRun, progress: legacyProgress, cancellationToken: cancellationToken);

    [McpServerTool(
        Name = PlaceTool,
        Title = "Place a device on an EPLAN page",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DevicePlaceResult))]
    [Description(
        "Places part devices, library symbols (including NO/NC, PLC), terminals, SPECIAL/DC black boxes, SPECIAL/BBDEF busbar definitions or dashed location boxes on circuit pages. " +
        "Devices inside a location box keep their own structure; no devices are renamed. x/y: mm, bottom left, y up. " +
        "One Ctrl+Z undoes it. Omit dry_run to preview; false executes. Engineer approval outside scratch; denial: APPROVAL_DENIED. See usage.")]
    public static async Task<CallToolResult> DevicePlaceAsync(
        WritePipeline pipeline,
        IBridgeClient bridge,
        string project,
        string page,
        double x,
        double y,
        string? tag = null,
        string? part = null,
        string? variant = null,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        string? library = null,
        string? symbol = null,
        int? symbol_variant = null,
        [Description("Relative displays")] JsonElement[]? display_properties = null,
        string[]? connection_designations = null,
        string[]? connection_descriptions = null,
        string? location_box = null,
        double? corner_x = null,
        double? corner_y = null,
        string? terminal_strip = null,
        string? terminal_number = null,
        string? terminal_strip_text = null,
        double[]? terminal_strip_position = null,
        [Description("PLC point suffix; tag is existing card")] string? plc_connection_point = null,
        string? plc_address = null,
        string? plc_symbolic_address = null,
        string? plc_text = null,
        string? plc_plug_designation = null,
        [Description("Open page first; UI only.")] bool show_page = false,
        IProgress<ProgressNotificationValue>? progress = null,
        PackLibrary? packs = null,
        [Description("Join existing tag as secondary symbol.")] bool? join_device = null,
        [AllowedValues("refuse", "warn")] string layout_guard = "refuse",
        CancellationToken cancellationToken = default)
    {
        var args = new DevicePlaceArgs
        {
            Project = project,
            Page = page ?? string.Empty,
            Part = part,
            Variant = ToolArguments.Optional(variant),
            X = x,
            Y = y,
            Tag = tag,
            JoinDevice = join_device,
            LayoutGuard = layout_guard,
            Library = library?.Trim(),
            Symbol = symbol?.Trim(),
            SymbolVariant = symbol_variant,
            ConnectionDesignations = connection_designations,
            ConnectionDescriptions = connection_descriptions,
            LocationBox = location_box,
            CornerX = corner_x,
            CornerY = corner_y,
            TerminalStrip = terminal_strip,
            TerminalNumber = terminal_number,
            TerminalStripText = terminal_strip_text,
            TerminalStripPosition = terminal_strip_position,
            PlcConnectionPoint = plc_connection_point,
            PlcAddress = plc_address,
            PlcSymbolicAddress = plc_symbolic_address,
            PlcPlugDesignation = plc_plug_designation,
            PlcText = plc_text
        };
        List<string> violations = DevicePlaceInputs.Check(args, display_properties);
        if (violations.Count > 0)
        {
            BridgeError error = ToolResults.InvalidArgumentError(violations);
            error.Execution = ExecutionState.NotStarted;
            return ToolResults.Error(error);
        }
        PluginResolution? resolution = null;
        if (packs is not null && args.TerminalStripPosition is not null)
        {
            try
            {
                string path = Path.IsPathFullyQualified(project) ? project
                    : (await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet, new() { Project = project }, cancellationToken).ConfigureAwait(false)).Project.Path;
                resolution = await new PluginResolver(packs).ResolveAsync(path, cancellationToken, page: args.Page).ConfigureAwait(false);
                args.StripDefinition = DevicePluginDefaults.Select(resolution);
            }
            catch (BridgeCallException ex)
            { return ToolResults.Error(ex.Error); }
        }
        List<string> pluginWarnings = resolution?.Warnings.ToList() ?? [];
        var request = new WriteRequest<DevicePlaceArgs, DevicePlaceArgs>(
            PlaceTool,
            ToolTier.Write,
            OperationNames.DevicePlacePlan,
            OperationNames.DevicePlaceExecute,
            args,
            plan => new DevicePlaceArgs
            {
                Project = args.Project,
                Page = args.Page,
                Part = args.Part,
                Variant = args.Variant,
                X = args.X,
                Y = args.Y,
                Tag = args.Tag,
                JoinDevice = args.JoinDevice,
                LayoutGuard = args.LayoutGuard,
                Library = args.Library,
                Symbol = args.Symbol,
                SymbolVariant = args.SymbolVariant,
                DisplayProperties = args.DisplayProperties,
                ConnectionDesignations = args.ConnectionDesignations,
                ConnectionDescriptions = args.ConnectionDescriptions,
                LocationBox = args.LocationBox,
                CornerX = args.CornerX,
                CornerY = args.CornerY,
                TerminalStrip = args.TerminalStrip,
                TerminalNumber = args.TerminalNumber,
                TerminalStripText = args.TerminalStripText,
                TerminalStripPosition = args.TerminalStripPosition,
                StripDefinition = args.StripDefinition,
                PlcConnectionPoint = args.PlcConnectionPoint,
                PlcAddress = args.PlcAddress,
                PlcSymbolicAddress = args.PlcSymbolicAddress,
                PlcText = args.PlcText,
                PlcPlugDesignation = args.PlcPlugDesignation,
                Fingerprint = plan.Fingerprint
            },
            dry_run,
            PageWriteTools.DefaultLimit,
            "Placing the device")
        {
            BeforeExecute = packs is not null && resolution is not null
                ? PluginConsumerDefaults.BeforeExecute(packs, resolution, args.StripDefinition?.Plugins ?? [], pluginWarnings) : null
        };
        PageDisplay display = await PageDisplay.ShowAsync(bridge, show_page, args.Project, args.Page, cancellationToken).ConfigureAwait(false);
        WriteOutcome<DevicePlaceData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<DevicePlaceArgs, DevicePlaceArgs, DevicePlaceData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return display.Apply(ToolResults.Error(ex.Error));
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        DevicePlaceData? data = outcome.Data;
        TerminalPlacementData? terminalData = data?.Terminal ?? (details.TryGetValue("terminal", out string? terminal) ? JsonSerializer.Deserialize<TerminalPlacementData>(terminal, HostJson.Options) : null);
        var result = new DevicePlaceResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Page = ResultShaper.CleanText(data?.Page ?? outcome.Plan.Changes.FirstOrDefault()?.Name ?? args.Page),
            Part = ResultShaper.CleanText(data?.Part ?? details.GetValueOrDefault("part") ?? part ?? string.Empty),
            Variant = ResultShaper.CleanText(data?.Variant ?? details.GetValueOrDefault("variant") ?? string.Empty),
            Device = new DevicePlaceResult.DeviceEntry
            {
                Tag = ResultShaper.CleanText(data?.Tag ?? details.GetValueOrDefault("tag") ?? string.Empty),
                VisibleTag = ResultShaper.CleanText(data?.VisibleTag),
                Category = ResultShaper.CleanText(data?.Category),
                Type = ResultShaper.CleanText(data?.Type),
                IsMainFunction = data?.IsMainFunction,
            },
            Terminal = terminalData,
            Metadata = terminalData is { StripCreated: true, StripPosition: not null } && args.StripDefinition is { Plugins.Length: > 0 } used
                ? new JsonObject { ["plugins"] = JsonSerializer.SerializeToNode(used.Plugins) } : null,
            Plc = data?.Plc,
            BlackBox = data is not null ? data.BlackBox : details.TryGetValue("blackBox", out string? blackBox) ? JsonSerializer.Deserialize<BlackBoxData>(blackBox, HostJson.Options) : null,
            LocationBox = data?.LocationBox ?? (details.TryGetValue("locationBox", out string? box) ? JsonSerializer.Deserialize<LocationBoxData>(box, HostJson.Options) : null),
            Functions = data?.Functions ?? (args.LocationBox is not null ? 0 : terminalData is not null ? (terminalData.StripCreated ? 2 : 1) : args.Library is not null ? 1 : null),
            Unplaced = data?.Unplaced ?? (args.Library is not null || args.LocationBox is not null || args.TerminalStrip is not null ? 0 : null),
            UnplacedFunctions = [.. (data?.UnplacedFunctions ?? []).Select(name => ResultShaper.CleanText(name))],
            Symbol = details.TryGetValue("symbol", out string? resolved) ? JsonSerializer.Deserialize<SymbolSearchItem>(resolved, HostJson.Options) : null,
            Changes = args.Library is not null || args.LocationBox is not null || args.TerminalStrip is not null ? [.. outcome.Plan.Changes.Select(c => new PlannedChange { Action = c.Action, Kind = c.Kind, Name = ResultShaper.CleanText(c.Name), Property = ResultShaper.CleanText(c.Property), NewValue = ResultShaper.CleanText(c.NewValue) })] : null,
            Undo = committed ? "AI: " + PlaceTool : null,
            Recovered = outcome.Recovered,
            Hint = dry_run ? WriteToolDefaults.ExecutionHint + " " + PlaceHint(outcome, details) : PlaceHint(outcome, details),
        };
        if (pluginWarnings.Count > 0)
        {
            result.Metadata ??= new JsonObject();
            result.Metadata["warnings"] = JsonSerializer.SerializeToNode(pluginWarnings.Select(ResultShaper.CleanText));
        }
        if (result.Symbol is { } plannedSymbol)
        {
            plannedSymbol.Library = ResultShaper.CleanText(plannedSymbol.Library);
            plannedSymbol.Name = ResultShaper.CleanText(plannedSymbol.Name);
            plannedSymbol.Description = ResultShaper.CleanText(plannedSymbol.Description);
        }
        if (result.Terminal is { } placedTerminal)
        { placedTerminal.Strip = ResultShaper.CleanText(placedTerminal.Strip); placedTerminal.Owner = ResultShaper.CleanText(placedTerminal.Owner); placedTerminal.Number = ResultShaper.CleanText(placedTerminal.Number); placedTerminal.SortCode = ResultShaper.CleanText(placedTerminal.SortCode); }
        if (args.PlcConnectionPoint is not null)
        {
            result.Functions = null;
            result.Unplaced = null; // A55: implicit representations are verified after commit.
            result.Hint = "Verify committed PLC membership, calculated symbolic address and cardReference with eplan_plc_get response_format=detailed; unset references are not synthesized. " + result.Hint;
        }
        if (result.Plc is { } plc)
        {
            plc.Name = ResultShaper.CleanText(plc.Name);
            plc.Page = ResultShaper.CleanText(plc.Page);
            plc.Owner = ResultShaper.CleanText(plc.Owner);
            plc.Address = ResultShaper.CleanText(plc.Address);
            plc.ManualSymbolicAddress = ResultShaper.CleanText(plc.ManualSymbolicAddress);
            plc.SymbolicAddress = ResultShaper.CleanText(plc.SymbolicAddress);
            plc.Text = ResultShaper.CleanText(plc.Text);
            plc.CardReference = ResultShaper.CleanText(plc.CardReference);
            plc.MainReference = ResultShaper.CleanText(plc.MainReference);
            plc.PlugDesignation = ResultShaper.CleanText(plc.PlugDesignation);
            plc.DataType = ResultShaper.CleanText(plc.DataType);
        }
        if (result.BlackBox is { } black)
        { black.Tag = ResultShaper.CleanText(black.Tag); black.FunctionsInside = black.FunctionsInside?.Select(name => ResultShaper.CleanText(name) ?? string.Empty).ToArray(); }
        if (result.LocationBox is { } location)
        { location.Structure = ResultShaper.CleanText(location.Structure); result.Hint = "Devices inside keep their own structure; affected devices: none. " + result.Hint; }
        string summary = args.LocationBox is not null ? (committed ? "Placed one dashed location box; affected devices: none. Ctrl+Z undoes this step." : "Dry run: would place one dashed location box; affected devices: none. Nothing was changed.") : !committed ? "Dry run: would place one device. Nothing was changed."
            : args.PlcConnectionPoint is not null ? "Placed one PLC point; verify it with eplan_plc_get. Ctrl+Z undoes this step."
            : data is null ? "Placed a device; Ctrl+Z in EPLAN undoes this step."
            : $"Placed a device with {ToolResults.Count(data.Functions, "function")}; Ctrl+Z in EPLAN undoes this step.";
        return display.Apply(ToolResults.Success(summary, result));
    }

    /// <summary>The hints of the tool spec (section 3), each when it applies, in one text.</summary>
    private static string? PlaceHint(WriteOutcome<DevicePlaceData> outcome, Dictionary<string, string> details)
    {
        DevicePlaceData? data = outcome.Data;
        var hints = new List<string>();
        if (outcome.Recovered)
        {
            hints.Add(details.ContainsKey("locationBox") ? "The response was lost, but EPLAN reported the box as placed; verify it with eplan_page_get and include_drawings." : "The response was lost, but EPLAN reported the device as placed; check it with eplan_device_get.");
        }
        else if (data is { Unplaced: > 0 } placed)
        {
            hints.Add($"{ToolResults.Count(placed.Unplaced, "function")} of the device remain unplaced, as with EPLAN Insert Device. For a whole PLC module, use eplan_macro_insert with part for its assembly macro. For multiple symbols, place them with the same tag, then assign part with eplan_device_update.");
        }

        if (!(data?.PageSizeKnown ?? details.GetValueOrDefault("pageSizeKnown") == "true"))
        {
            hints.Add("EPLAN cannot tell this page's size (a new, empty page), so only x >= 0 and y >= 0 were checked: check in EPLAN that the device lies within the page's frame.");
        }

        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        return hints.Count == 0 ? null : string.Join(" ", hints);
    }

    private static DeviceUpdateResult.DeviceEntry Device(DeviceUpdateData data) => new()
    {
        Tag = ResultShaper.CleanText(data.Tag),
        PreviousTag = ResultShaper.CleanText(data.PreviousTag),
        VisibleTag = ResultShaper.CleanText(data.VisibleTag),
        Functions = data.Functions,
        Pages = [.. data.Pages.Select(p => ResultShaper.CleanText(p))],
        Part = ResultShaper.CleanText(data.Part),
        Variant = ResultShaper.CleanText(data.Variant),
    };

    /// <summary>The device as the plan has it: the change and the details device.update.plan adds.</summary>
    private static DeviceUpdateResult.DeviceEntry Planned(WritePlan plan)
    {
        PlannedChange? change = plan.Changes.FirstOrDefault();
        Dictionary<string, string> details = plan.Details ?? [];
        return new DeviceUpdateResult.DeviceEntry
        {
            Tag = ResultShaper.CleanText(details.GetValueOrDefault("tag") ?? change?.NewValue ?? string.Empty),
            PreviousTag = ResultShaper.CleanText(details.GetValueOrDefault("previousTag") ?? change?.OldValue ?? string.Empty),
            Functions = int.TryParse(details.GetValueOrDefault("functions"), NumberStyles.None, CultureInfo.InvariantCulture, out int functions) ? functions : 0,
            Pages = details.GetValueOrDefault("pages") is { Length: > 0 } pages ? [.. pages.Split('\n').Select(p => ResultShaper.CleanText(p))] : [],
            Part = ResultShaper.CleanText(details.GetValueOrDefault("part")) is { Length: > 0 } part ? part : null,
            Variant = ResultShaper.CleanText(details.GetValueOrDefault("variant")) is { Length: > 0 } variant ? variant : null,
        };
    }
}
