using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
    [Description(
        "Creates pages in an EPLAN project; one Ctrl+Z in EPLAN undoes them all. Reuse the name structure eplan_page_list " +
        "shows. Outside scratch it needs the engineer's approval. Example: {\"project\": " +
        "\"Line1\", \"pages\": [{\"name\": \"=GAA+A1&EFS1/5\", \"description\": \"Power\"}]}.")]
    public static async Task<CallToolResult> PageCreateAsync(
        WritePipeline pipeline,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("1-50, created in order")] NewPage[] pages,
        [Description("e.g. zh_CN; default: EPLAN UI")] string? language = null,
        [Description("Only return the plan")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Violations(project, pages?.Length, language) is { Count: > 0 } violations)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new PageCreateArgs
        {
            Project = project,
            Pages = [.. pages!.Select(p => new PageCreateItem { Name = p.Name ?? string.Empty, PageType = PageType(p.PageType), Description = ToolArguments.Optional(p.Description) })],
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
            Pages = outcome.Data is { } data
                ? [.. data.Pages.Select(p => Entry(p.Name, p.Type, p.Description))]
                : [.. args.Pages.Select(p => Entry(p.Name, p.PageType ?? _pageTypes[0], p.Description))],
            Undo = committed ? "AI: " + CreateTool : null,
            Recovered = outcome.Recovered,
            Hint = outcome.Recovered
                ? "The response was lost, but EPLAN reported the pages as created; check them with eplan_page_list."
                : ResultShaper.CleanText(outcome.Data?.Warning),
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
    [Description(
        "Changes the descriptions of pages in one language and keeps the other languages; one Ctrl+Z in EPLAN undoes it. " +
        "Pages that already read so are left alone. Outside scratch it needs the engineer's approval. Example: " +
        "{\"project\": \"Line1\", \"pages\": [{\"name\": \"=GAA+A1&EFS1/3\", \"description\": \"Main circuit\"}], \"language\": \"en_US\"}.")]
    public static async Task<CallToolResult> PageUpdateAsync(
        WritePipeline pipeline,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("1-50 pages")] PageText[] pages,
        [Description("The one language to change; default: EPLAN UI")] string? language = null,
        [Description("Only return the plan")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Violations(project, pages?.Length, language) is { Count: > 0 } violations)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new PageUpdateArgs
        {
            Project = project,
            Pages = [.. pages!.Select(p => new PageUpdateItem { Name = p.Name ?? string.Empty, Description = p.Description ?? string.Empty })],
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
            Language = outcome.Data?.Language ?? LanguageOf(outcome.Plan) ?? args.Language,
            Pages = outcome.Data is { } data
                ? [.. data.Pages.Select(p => Change(p.Name, p.Description, p.Previous))]
                : [.. outcome.Plan.Changes.Select(c => Change(c.Name, c.NewValue, c.OldValue))],
            Unchanged = outcome.Data?.Unchanged ?? (args.Pages.Count - outcome.Plan.Changes.Count),
            Undo = committed ? "AI: " + UpdateTool : null,
            Recovered = outcome.Recovered,
            Hint = outcome.Recovered
                ? "The response was lost, but EPLAN reported the change as committed; check the descriptions with eplan_page_get."
                : ResultShaper.CleanText(outcome.Data?.Warning),
        };
        string count = ToolResults.Count(result.Pages.Count, "page");
        string summary = committed ? $"Updated the descriptions of {count}; Ctrl+Z in EPLAN undoes this step."
            : result.Pages.Count == 0 ? $"Nothing to change: the {ToolResults.Count(result.Unchanged, "page")} already have these descriptions."
            : $"Dry run: would update {count}. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>The default execution limit of a write (M3 design 4.3).</summary>
    internal static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(60);

    /// <summary>The execution limit (M3 design 4.3): 60 s, plus 2 s per page, at most 300 s.</summary>
    internal static TimeSpan Limit(int pages) => TimeSpan.FromSeconds(Math.Min(300, 60 + (2 * pages)));

    /// <summary>What the Host checks before planning; the Bridge checks the pages themselves, all at once.</summary>
    private static List<string> Violations(string? project, int? pages, string? language)
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

    private static PageUpdateResult.PageEntry Change(string name, string? description, string? previous) => new()
    {
        Name = ResultShaper.CleanText(name),
        Description = ResultShaper.CleanText(description),
        Previous = ResultShaper.CleanText(previous),
    };

    /// <summary>A known page type in its exact spelling; anything else as given, for the Bridge to report.</summary>
    private static string? PageType(string? given) =>
        ToolArguments.Optional(given) is { } type ? _pageTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)) ?? type : null;

    private static PageCreateResult.PageEntry Entry(string name, string type, string? description) => new()
    {
        Name = ResultShaper.CleanText(name),
        Type = ResultShaper.CleanText(type),
        Description = ResultShaper.CleanText(description),
    };

    /// <summary>One element of <c>pages</c>.</summary>
    internal sealed class NewPage
    {
        [Description("Full page name")]
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [AllowedValues("Circuit", "CircuitSingleLine", "Overview", "Graphics")]
        [JsonPropertyName("page_type")]
        public string? PageType { get; set; }

        [Description("Max 500 chars")]
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    /// <summary>One element of eplan_page_update's <c>pages</c>.</summary>
    internal sealed class PageText
    {
        [Description("Full page name")]
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [Description("New text, max 500 chars; empty clears it")]
        [Required]
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }
}
