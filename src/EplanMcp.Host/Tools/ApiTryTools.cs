using System.ComponentModel;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
namespace EplanMcp.Host.Tools;
[McpServerToolType]
internal sealed class ApiTryTools(IBridgeClient bridge, WritePipeline writes, HostConfig config, ApiIndex index)
{
    internal const string Tool = "eplan_api_try";
    private static readonly string[] _locatorFields = ["digest", "oid", "page", "project", "type"];
    [McpServerTool(Name = Tool, Title = "Try an unverified EPLAN API group", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApiTryResult))]
    [Description("Trials one unverified catalog group on scratch with per-call approval and snapshot comparison. full + apiTry:on; scalar reads only. target comes from eplan_object_find. Prefer verified task tools. Example: {\"group\":\"O:Eplan.EplApi.DataModel.Function.Create\",\"project\":\"Scratch\",\"parameters\":{},\"dry_run\":true}. See eplan_tool_usage.")]
    public async Task<CallToolResult> TryAsync(IProgress<ProgressNotificationValue> progress, string group, string project, JsonElement parameters, bool dry_run = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (config.ApiTry != ActionRunSwitch.On) { throw ActionRunCatalog.Denied("apiTry is off in config.json."); }
            ApiEntry? entry = index.Data.Entries.FirstOrDefault(e => e.Row.Group == group);
            if (entry is null || entry.Row.ExcludedReason is not null || !ApiTryCatalog.Allowed(group))
            { throw ActionRunCatalog.Denied("This API group is unknown or excluded from trial execution."); }
            if (string.IsNullOrWhiteSpace(project) || parameters.ValueKind != JsonValueKind.Object || parameters.GetRawText().Length > 2048
                || parameters.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != parameters.EnumerateObject().Count()
                || parameters.EnumerateObject().Any(p => p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Null || p.Value.ValueKind == JsonValueKind.Object && (p.Name != "target" || !Locator(p.Value))))
            { return ToolResults.Error(new BridgeError { Reason = ErrorReason.InvalidArgument, Execution = ExecutionState.NotStarted, Message = "Supply project and a parameter object (2048 characters maximum) containing only simple values and target locator." }); }
            ProjectGetData resolved = await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet, new() { Project = project }, cancellationToken).ConfigureAwait(false);
            if (!ScratchPaths.IsInside(resolved.Project.Path, config.ScratchRoots)) { throw ActionRunCatalog.Denied("API trials are allowed only in scratch projects."); }
            var args = new ApiTryArgs { Group = group, Project = resolved.Project.Path, Parameters = parameters.GetRawText() };
            var request = new WriteRequest<ApiTryArgs, ApiTryArgs>(Tool, ToolTier.Write, OperationNames.ApiTryPlan, OperationNames.ApiTryExecute, args,
                p => new() { Group = group, Project = p.ProjectPath, Parameters = args.Parameters, Fingerprint = p.Fingerprint }, dry_run, TimeSpan.FromSeconds(30), "Trying unverified API") { Unverified = true };
            WriteOutcome<ApiTryData> outcome = await writes.RunAsync<ApiTryArgs, ApiTryArgs, ApiTryData>(request, progress, cancellationToken).ConfigureAwait(false);
            ApiTryData? data = outcome.Data;
            string[] changed = data is null ? [] : [.. data.Before.Keys.Union(data.After.Keys).Where(k => data.Before.GetValueOrDefault(k) != data.After.GetValueOrDefault(k)).Order(StringComparer.Ordinal)];
            string? Hash(Dictionary<string, string>? state) => state is null ? null : WritePipeline.HashArguments(state.OrderBy(p => p.Key, StringComparer.Ordinal));
            return ToolResults.Success("Unverified API trial: " + outcome.Execution + ".", new ApiTryResult(outcome.Plan.Project, group, outcome.Plan.Details!["status"], true, outcome.Execution, outcome.Recovered,
                ResultShaper.CleanText(data?.Value), Hash(data?.Before), Hash(data?.After), changed.Take(20).Select(k => new ApiTryChange(ResultShaper.CleanText(k)!, ResultShaper.CleanText(data!.Before.GetValueOrDefault(k)), ResultShaper.CleanText(data!.After.GetValueOrDefault(k)))).ToArray(), changed.Length, changed.Length > 20,
                "Unverified; snapshots omit unplaced objects and 3D connections. Equal observations do not prove absence of side effects. Recovered calls have no inferred readback; undo is unverified."));
        }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return ToolResults.Error(ex.Error); }
    }
    private static bool Locator(JsonElement value) => value.EnumerateObject().Count() == 5
        && value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(_locatorFields, StringComparer.Ordinal)
        && value.GetProperty("project").ValueKind == JsonValueKind.String && value.GetProperty("page").ValueKind == JsonValueKind.String
        && value.GetProperty("type").ValueKind == JsonValueKind.Number && value.GetProperty("type").TryGetInt32(out int type) && type > 0
        && value.GetProperty("oid").ValueKind == JsonValueKind.Number && value.GetProperty("oid").TryGetInt64(out long oid) && oid > 0
        && value.GetProperty("digest").ValueKind == JsonValueKind.String && System.Text.RegularExpressions.Regex.IsMatch(value.GetProperty("digest").GetString()!, "\\A[0-9a-f]{64}\\z");
}
internal sealed record ApiTryChange(string Key, string? Before, string? After);
internal sealed record ApiTryResult(string Project, string Group, string Status, bool Unverified, string Execution, bool Recovered, string? Value, string? Before, string? After, IReadOnlyList<ApiTryChange> Changes, int TotalChanges, bool Truncated, string Hint);
