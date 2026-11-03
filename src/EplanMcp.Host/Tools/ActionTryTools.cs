using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class ActionTryTools(ActionCatalogReader catalog, WritePipeline writes, HostConfig config)
{
    public const string Tool = "eplan_action_try";
    [McpServerTool(Name = Tool, Title = "Try an unverified catalog action", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(JsonObject))]
    [Description("Try a catalog action in scratch (full, actionTry:on). Templates: eplan_action_catalog_get. selection: same-page locators; [] clears. Omit/true previews; false needs per-call approval (APPROVAL_DENIED). Never retry unknown outcomes. Example: {\"project\":\"Scratch\",\"action\":\"XGedZoomAllAction\"}")]
    public async Task<CallToolResult> TryAsync(string project, string action, string? type = null, JsonElement? parameters = null, bool dry_run = WriteToolDefaults.Preview,
        List<DrawingLocator>? selection = null, IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        ActionCatalogData? definitions = null;
        try
        {
            if (config.ActionTry != ActionRunSwitch.On)
            { throw ActionRunCatalog.Denied("Action trials are disabled."); }
            definitions = await catalog.ReadAsync(cancellationToken).ConfigureAwait(false);
            ActionTryArgs args = Check(definitions, project, action, type, parameters ?? default);
            if (selection is not null && (!SelectionTools.Valid(selection, selection.Count == 0 ? "clear" : "replace")
                || JsonSerializer.Serialize(JsonSerializer.Serialize(selection, HostJson.Options)).Length > 55000))
            { throw new BridgeCallException(ToolResults.InvalidArgumentError(["selection must contain unique same-page discovery locators, at most 200 objects and 20000 JSON characters."])); }
            args.Selection = selection;
            var request = new WriteRequest<ActionTryArgs, ActionTryArgs>(Tool, ToolTier.Write, OperationNames.ActionTryPlan, OperationNames.ActionTryExecute, args,
                p => new() { Project = args.Project, Action = args.Action, Type = args.Type, Parameters = args.Parameters, Selection = args.Selection, Fingerprint = p.Fingerprint }, dry_run, TimeSpan.FromSeconds(30), "Trying unverified action")
            { Unverified = true };
            WriteOutcome<ActionRunData> outcome = await writes.RunAsync<ActionTryArgs, ActionTryArgs, ActionRunData>(request, progress, cancellationToken).ConfigureAwait(false);
            ActionRunData before = JsonSerializer.Deserialize<ActionRunData>(outcome.Plan.Details!["actionRun"], HostJson.Options)!;
            var after = outcome.Data?.After;
            var result = new JsonObject
            {
                ["action"] = action,
                ["type"] = type,
                ["project"] = outcome.Plan.Project,
                ["unverified"] = true,
                ["execution"] = outcome.Execution,
                ["dryRun"] = dry_run,
                ["fingerprint"] = outcome.Plan.Fingerprint,
                ["changes"] = JsonSerializer.SerializeToNode(outcome.Plan.Changes.Select(c => new PlannedChange { Action = c.Action, Kind = c.Kind, Name = ResultShaper.CleanText(c.Name), Property = ResultShaper.CleanText(c.Property), OldValue = ResultShaper.CleanText(c.OldValue), NewValue = ResultShaper.CleanText(c.NewValue) }), HostJson.Options),
                ["recovered"] = outcome.Recovered,
                ["before"] = Evidence(before.Before),
                ["after"] = after is null ? null : Evidence(after),
                ["changed"] = after is null ? null : !before.Before.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(after.OrderBy(p => p.Key, StringComparer.Ordinal)),
                ["truncated"] = before.Before.Count > 20 || after?.Count > 20,
                ["hint"] = "unverified; fingerprint comparison only. Rollback and Ctrl+Z are unverified. " + ResultShaper.CleanText(outcome.Data?.Warning) + (dry_run ? " " + WriteToolDefaults.ExecutionHint : string.Empty),
            };
            return ToolResults.Success(dry_run ? "Unverified action planned; nothing executed." : "Unverified action finished; inspect the fingerprint comparison.", result);
        }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return ToolResults.Error(ex.Error, definitions is not null && ex.Error.Reason == ErrorReason.InvalidArgument ? ActionCallTemplates.Closest(definitions, action, type, parameters ?? default) : null); }
    }
    private static JsonObject Evidence(Dictionary<string, string> state) => new(state.Where(p => p.Key != "target").OrderBy(p => p.Key, StringComparer.Ordinal).Take(20)
        .Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(ResultShaper.CleanText(p.Value)))));
    internal static ActionTryArgs Check(ActionCatalogData catalog, string project, string action, string? type, JsonElement parameters)
    {
        ActionCatalogItem[] rows = [.. catalog.Items.Where(i => i.Name == action && i.Type == type)];
        if (!ActionTryPolicy.Name(action) || ActionTryPolicy.Denied("action", action + "/" + type) || rows.Length == 0
            || rows.Any(i => i.ExcludedReason is not null || i.WhitelistEntry is not null)
            || catalog.Items.Any(i => i.ExcludedReason is not null && string.Equals(i.Name, action, StringComparison.OrdinalIgnoreCase) && string.Equals(i.Type, type, StringComparison.OrdinalIgnoreCase))
            || ActionRunCatalog.Entries.Values.Any(e => string.Equals(ActionRunCatalog.Text(e, "action"), action, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e["fixed"]?["TYPE"]?.GetValue<string>(), type, StringComparison.OrdinalIgnoreCase)))
        { throw ActionRunCatalog.Denied("This catalog action is excluded from trials."); }
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        { problems.Add("project is required."); }
        if (type is not null && !ActionTryPolicy.Name(type))
        { problems.Add("type must exactly match the catalog TYPE."); }
        if (parameters.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
        { problems.Add("parameters must be a flat object."); }
        JsonProperty[] given = parameters.ValueKind == JsonValueKind.Object ? [.. parameters.EnumerateObject()] : [];
        if (given.Length > 32 || given.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        { problems.Add("Use at most 32 unique parameter names."); }
        if (!rows.Any(r => given.All(p => r.Parameters.Any(k => k.GetProperty("name").GetString() == p.Name))))
        { problems.Add("Every parameter name must be known in the same catalog row."); }
        var args = new ActionTryArgs { Action = action, Project = project, Type = type };
        foreach (JsonProperty p in given)
        {
            string? value = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? p.Value.GetRawText() : null;
            if (!ActionTryPolicy.Name(p.Name) || p.Name.Equals("TYPE", StringComparison.OrdinalIgnoreCase) || ActionTryPolicy.Denied("parameter", p.Name) || !ActionTryPolicy.Value(value))
            { problems.Add("A parameter is invalid; use simple catalog values without paths, control characters, quotes or target overrides."); }
            else
            { args.Parameters.Add(new() { Name = p.Name, Value = value! }); }
        }
        if (problems.Count > 0)
        { throw new BridgeCallException(ToolResults.InvalidArgumentError(problems)); }
        return args;
    }
}
