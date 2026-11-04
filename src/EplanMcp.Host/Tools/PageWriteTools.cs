using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The page write tools (docs/tools/eplan_page_create.md, eplan_page_update.md). Every call goes through
/// <see cref="WritePipeline"/>: plan, policy, approval, audit, one execution that is never sent twice. The Bridge checks
/// the pages themselves.
/// </summary>
[McpServerToolType]
internal sealed class PageWriteTools
{
    public const string CreateTool = "eplan_page_create";
    public const string UpdateTool = "eplan_page_update";
    public const int MaxPages = 50;

    /// <summary>The page types the tool offers, the default first; the Bridge checks them again.</summary>
    private static readonly string[] _pageTypes = ["Circuit", "CircuitSingleLine", "Overview", "Graphics"];

    [McpServerTool(
        Name = CreateTool,
        Title = "Create pages in an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PageCreateResult))]
    [Description("Creates pages. Omit/true previews; false executes. One undo; approval outside scratch.")]
    public static async Task<CallToolResult> PageCreateAsync(
        WritePipeline pipeline,
        [Description("Open project")] string project,
        [Description("1-50 pages")] NewPage[] pages,
        [Description("Text language")] string? language = null,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Violations(project, pages?.Length, language) is { Count: > 0 } violations)
        {
            return ToolResults.InvalidArguments(violations);
        }

        if (DescriptionViolations(pages!.Select(p => p.Description)) is { Count: > 0 } descriptions)
        { return ToolResults.InvalidArguments(descriptions); }
        var args = new PageCreateArgs
        {
            Project = project,
            Pages = [.. pages!.Select(p => new PageCreateItem { Name = p.Name ?? string.Empty, PageType = PageType(p.PageType), Description = ToolArguments.Optional(DescriptionText(p.Description)), DescriptionTranslations = DescriptionMap(p.Description), GridSizeMm = p.GridSizeMm })],
            Language = ToolArguments.Optional(language),
        };
        var request = new WriteRequest<PageCreateArgs, PageCreateArgs>(
            CreateTool,
            ToolTier.Write,
            OperationNames.PageCreatePlan,
            OperationNames.PageCreateExecute,
            args,
            plan => new PageCreateArgs { Project = args.Project, Pages = args.Pages, Language = args.Language, Fingerprint = plan.Fingerprint },
            dry_run,
            Limit(args.Pages.Count),
            "Creating pages");
        WriteOutcome<PageCreateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<PageCreateArgs, PageCreateArgs, PageCreateData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        var result = new PageCreateResult
        {
            Project = ResultShaper.CleanText(outcome.Data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Pages = outcome.Data is { } data
                ? [.. data.Pages.Select(p => Entry(p.Name, p.Type, p.Description, p.GridSizeMm))]
                : [.. args.Pages.Select(p => Entry(p.Name, p.PageType ?? _pageTypes[0], p.Description, p.GridSizeMm))],
            Undo = committed ? "AI: " + CreateTool : null,
            Recovered = outcome.Recovered,
            Hint = outcome.Recovered
                ? "The response was lost, but EPLAN reported the pages as created; check them with eplan_page_list."
                : dry_run ? WriteToolDefaults.ExecutionHint : ResultShaper.CleanText(outcome.Data?.Warning),
        };
        string count = ToolResults.Count(result.Pages.Count, "page");
        return ToolResults.Success(
            committed ? $"Created {count}; Ctrl+Z in EPLAN undoes this step." : $"Dry run: would create {count}. Nothing was changed.",
            result);
    }

    [McpServerTool(
        Name = UpdateTool,
        Title = "Change page descriptions in an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PageUpdateResult))]
    [Description("Updates page text, frame or grid. Omit/true previews; false executes. One undo; approval outside scratch.")]
    public static async Task<CallToolResult> PageUpdateAsync(
        WritePipeline pipeline,
        [Description("Open project")] string project,
        [Description("1-50 pages")] PageText[] pages,
        [Description("Language")] string? language = null,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Violations(project, pages?.Length, language) is { Count: > 0 } violations)
        {
            return ToolResults.InvalidArguments(violations);
        }

        if (DescriptionViolations(pages!.Select(p => p.Description)) is { Count: > 0 } descriptions)
        { return ToolResults.InvalidArguments(descriptions); }
        var args = new PageUpdateArgs
        {
            Project = project,
            Pages = [.. pages!.Select(p => new PageUpdateItem { Name = p.Name ?? string.Empty, Description = DescriptionText(p.Description), DescriptionTranslations = DescriptionMap(p.Description), PlotFrame = p.PlotFrame, GridSizeMm = p.GridSizeMm })],
            Language = ToolArguments.Optional(language),
        };
        var request = new WriteRequest<PageUpdateArgs, PageUpdateArgs>(
            UpdateTool,
            ToolTier.Write,
            OperationNames.PageUpdatePlan,
            OperationNames.PageUpdateExecute,
            args,
            plan => new PageUpdateArgs { Project = args.Project, Pages = args.Pages, Language = args.Language, Fingerprint = plan.Fingerprint },
            dry_run,
            DefaultLimit,
            "Changing page descriptions");
        WriteOutcome<PageUpdateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<PageUpdateArgs, PageUpdateArgs, PageUpdateData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        var result = new PageUpdateResult
        {
            Project = ResultShaper.CleanText(outcome.Data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Language = outcome.Data?.Language ?? LanguageOf(outcome.Plan) ?? args.Language,
            Pages = outcome.Data is { } data
                ? [.. data.Pages.Select(p => Change(p.Name, p.Description, p.Previous, p.PlotFrame, p.PreviousPlotFrame, p.GridSizeMm, p.PreviousGridSizeMm))]
                : [.. outcome.Plan.Changes.GroupBy(c => c.Name).Select(group =>
                {
                    PlannedChange? description = group.FirstOrDefault(c => c.Property != "plot_frame" && c.Property != "grid_size_mm");
                    PlannedChange? frame = group.FirstOrDefault(c => c.Property == "plot_frame");
                    PlannedChange? grid = group.FirstOrDefault(c => c.Property == "grid_size_mm");
                    return Change(group.Key, description?.NewValue, description?.OldValue, frame?.NewValue, frame?.OldValue,
                        grid is null ? null : double.Parse(grid.NewValue!, CultureInfo.InvariantCulture), grid is null ? null : double.Parse(grid.OldValue!, CultureInfo.InvariantCulture));
                })],
            Unchanged = outcome.Data?.Unchanged ?? (args.Pages.Count - outcome.Plan.Changes.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count()),
            Undo = committed ? "AI: " + UpdateTool : null,
            Recovered = outcome.Recovered,
            Hint = outcome.Recovered
                ? "The response was lost, but EPLAN reported the change as committed; check the descriptions with eplan_page_get."
                : dry_run ? WriteToolDefaults.ExecutionHint : ResultShaper.CleanText(outcome.Data?.Warning),
        };
        string count = ToolResults.Count(result.Pages.Count, "page");
        string summary = committed ? $"Updated the descriptions of {count}; Ctrl+Z in EPLAN undoes this step."
            : result.Pages.Count == 0 ? $"Nothing to change: the {ToolResults.Count(result.Unchanged, "page")} already have these descriptions."
            : $"Dry run: would update {count}. Nothing was changed.";
        if (args.Pages.Any(p => p.PlotFrame is not null || p.GridSizeMm is not null))
        {
            summary = committed ? $"Updated {count}; Ctrl+Z in EPLAN undoes this step."
                : result.Pages.Count == 0 ? $"Nothing to change: requested values already match on {ToolResults.Count(result.Unchanged, "page")}."
                : $"Dry run: would update {count}. Nothing was changed.";
        }

        return ToolResults.Success(summary, result);
    }

    private static List<string> DescriptionViolations(IEnumerable<object?> values)
    {
        var errors = new List<string>();
        foreach ((object? value, int index) in values.Select((value, index) => (value, index)))
        {
            try
            { _ = DescriptionMap(value); }
            catch (ArgumentException) { errors.Add($"pages[{index}].description must be text or an object of language texts."); }
        }
        return errors;
    }

    private static string? DescriptionText(object? value) => value is string text ? text
        : value is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : null;

    private static Dictionary<string, string>? DescriptionMap(object? value)
    {
        if (value is null || value is string || value is JsonElement { ValueKind: JsonValueKind.String or JsonValueKind.Null })
        { return null; }
        JsonElement json = JsonSerializer.SerializeToElement(value);
        if (json.ValueKind != JsonValueKind.Object || json.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String))
        { throw new ArgumentException("description must be text or an object of language texts."); }
        return json.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
    }

    /// <summary>The default execution limit of a write (M3 design 4.3).</summary>
    internal static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(60);

    /// <summary>The execution limit (M3 design 4.3): 60 s, plus 2 s per page, at most 300 s.</summary>
    internal static TimeSpan Limit(int pages) => TimeSpan.FromSeconds(Math.Min(300, 60 + (2 * pages)));

    /// <summary>What the Host checks before planning; the Bridge checks the pages themselves, all at once.</summary>
    internal static List<string> Violations(string? project, int? pages, string? language)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (pages is null or < 1 or > MaxPages)
        {
            violations.Add($"'pages' must hold 1 to {MaxPages} pages.");
        }

        ToolArguments.CheckLanguage(language, violations);
        return violations;
    }

    /// <summary>The language a plan changes, from its <c>description (zh_CN)</c> property; null when it has no change.</summary>
    private static string? LanguageOf(WritePlan plan)
    {
        string? property = plan.Changes.FirstOrDefault()?.Property;
        int open = property?.IndexOf('(', StringComparison.Ordinal) ?? -1;
        return open >= 0 && property!.EndsWith(')') ? property[(open + 1)..^1] : null;
    }

    private static PageUpdateResult.PageEntry Change(string name, string? description, string? previous, string? frame = null, string? previousFrame = null, double? grid = null, double? previousGrid = null) => new()
    {
        Name = ResultShaper.CleanText(name),
        Description = ResultShaper.CleanText(description),
        Previous = ResultShaper.CleanText(previous),
        PlotFrame = ResultShaper.CleanText(frame),
        PreviousPlotFrame = ResultShaper.CleanText(previousFrame),
        GridSizeMm = grid,
        PreviousGridSizeMm = previousGrid,
    };

    /// <summary>A known page type in its exact spelling; anything else as given, for the Bridge to report.</summary>
    private static string? PageType(string? given) =>
        ToolArguments.Optional(given) is { } type ? _pageTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)) ?? type : null;

    private static PageCreateResult.PageEntry Entry(string name, string type, string? description, double? grid = null) => new()
    {
        Name = ResultShaper.CleanText(name),
        Type = ResultShaper.CleanText(type),
        GridSizeMm = grid,
        Description = ResultShaper.CleanText(description),
    };

    /// <summary>One element of <c>pages</c>.</summary>
    internal sealed class NewPage
    {
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [AllowedValues("Circuit", "CircuitSingleLine", "Overview", "Graphics")]
        [JsonPropertyName("page_type")]
        public string? PageType { get; set; }

        [Description("Text or language-to-text object; max 500 chars per language")]
        [JsonPropertyName("description")]
        public object? Description { get; set; }

        [JsonPropertyName("grid_size_mm")]
        public double? GridSizeMm { get; set; }
    }

    /// <summary>One element of eplan_page_update's <c>pages</c>.</summary>
    internal sealed class PageText
    {
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [Description("Text or language-to-text object; max 500 chars each; empty clears text")]
        [JsonPropertyName("description")]
        public object? Description { get; set; }

        [JsonPropertyName("grid_size_mm")]
        public double? GridSizeMm { get; set; }

        [JsonPropertyName("plot_frame")]
        public string? PlotFrame { get; set; }
    }
}
