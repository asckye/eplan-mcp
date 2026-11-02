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
internal sealed partial class ApiTryTools(IBridgeClient bridge, WritePipeline writes, HostConfig config, ApiIndex index, HostDataLocations? locations = null, ApiTryPages? pages = null)
{
    private readonly ApiTryPages _pages = pages ?? new(TimeProvider.System);
    internal const string Tool = "eplan_api_try";
    private static readonly string _hostSession = Session();
    private static string Session() { using var host = System.Diagnostics.Process.GetCurrentProcess(); return host.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + host.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Guid.NewGuid().ToString("N"); }
    private static readonly string[] _locatorFields = ["digest", "oid", "page", "project", "type"];
    [McpServerTool(Name = Tool, Title = "Try an unverified EPLAN API group", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApiTryResult))]
    [Description("Try scratch APIs, including create/remove; per-call approval, full + apiTry:on. Remove uses destructive approval; undo is unverified. eplan_api_get supplies try_template; INVALID_ARGUMENT includes the closest template. See eplan_tool_usage for examples.")]
    public async Task<CallToolResult> TryAsync(IProgress<ProgressNotificationValue> progress, string group, string project, JsonElement parameters, bool dry_run = false, string? result_cursor = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (config.ApiTry != ActionRunSwitch.On)
            { throw ActionRunCatalog.Denied("apiTry is off in config.json."); }
            ApiEntry? entry = index.Data.Entries.FirstOrDefault(e => e.Row.Group == group);
            if (entry is null || entry.Row.ExcludedReason is not null || !ApiTryCatalog.Allowed(group))
            { throw ActionRunCatalog.Denied("This API group is unknown or excluded from trial execution."); }
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("target", out _) && group.EndsWith(".Create", StringComparison.Ordinal) && !ApiTryCatalog.Service(group))
            { return ToolResults.Error(new BridgeError { Reason = ErrorReason.InvalidArgument, Execution = ExecutionState.NotStarted, Message = "Omit target for factory creation; Create cannot be called on an existing instance." }, ApiCallTemplates.Closest(index, group, parameters)); }
            if (string.IsNullOrWhiteSpace(project) || parameters.ValueKind != JsonValueKind.Object || parameters.GetRawText().Length > 131072
                || parameters.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != parameters.EnumerateObject().Count()
                || parameters.EnumerateObject().Any(p => Kind(p.Value) is "Object" or "Null" || p.Value.ValueKind == JsonValueKind.Array && ApiTryCatalog.MethodAccess(group) is null) || !MethodParameters(group, parameters))
            { return ToolResults.Error(new BridgeError { Reason = ErrorReason.InvalidArgument, Execution = ExecutionState.NotStarted, Message = "Supply project and the exact generated shape (131072 characters maximum), with finite PointD, locators/session handles and at most 200 supported elements per array." }, ApiCallTemplates.Closest(index, group, parameters)); }
            string query = PageCursor.Fingerprint(Tool, group, project, parameters.GetRawText());
            if (result_cursor is not null)
            { return ToolResults.Success("Cached API trial observations.", new ApiTryResult(project, group, "cached", true, ExecutionState.NotStarted, false, _pages.Read(result_cursor, query), null, null, [], 0, false, "Cached observations; no API call or approval was repeated.", [], null)); }
            ProjectGetData resolved = await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet, new() { Project = project }, cancellationToken).ConfigureAwait(false);
            if (!ScratchPaths.IsInside(resolved.Project.Path, config.ScratchRoots))
            { throw ActionRunCatalog.Denied("API trials are allowed only in scratch projects."); }
            ApiTryArgs args = FileArguments(group, resolved.Project.Path, parameters);
            args.HostSession = _hostSession;
            args.ScratchPath = ScratchPaths.Canonical(resolved.Project.Path);
            var request = new WriteRequest<ApiTryArgs, ApiTryArgs>(Tool, ApiTryLifecycle.Effect(group) == "removes" ? ToolTier.Destructive : ToolTier.Write, OperationNames.ApiTryPlan, OperationNames.ApiTryExecute, args,
                p => new() { Group = group, Project = p.ProjectPath, Parameters = args.Parameters, Fingerprint = p.Fingerprint, DataRoot = args.DataRoot, OutputId = args.OutputId, HostSession = args.HostSession, ScratchPath = args.ScratchPath }, dry_run, TimeSpan.FromSeconds(120), "Trying unverified API")
            { Unverified = true };
            WriteOutcome<ApiTryData> outcome = await writes.RunAsync<ApiTryArgs, ApiTryArgs, ApiTryData>(request, progress, cancellationToken).ConfigureAwait(false);
            ApiTryData? data = outcome.Data;
            string[] changed = data is null ? [] : [.. data.Before.Keys.Union(data.After.Keys).Where(k => k.StartsWith("property:", StringComparison.Ordinal) || data.Before.GetValueOrDefault(k) != data.After.GetValueOrDefault(k)).OrderBy(k => k.StartsWith("property:", StringComparison.Ordinal) ? 0 : 1).ThenBy(k => k.StartsWith("object:", StringComparison.Ordinal) && (!data.Before.ContainsKey(k) || !data.After.ContainsKey(k)) ? 0 : 1).ThenBy(k => k, StringComparer.Ordinal)];
            string? Hash(Dictionary<string, string>? state) => state is null ? null : WritePipeline.HashArguments(state.OrderBy(p => p.Key, StringComparer.Ordinal));
            return ToolResults.Success("Unverified API trial: " + outcome.Execution + ".", new ApiTryResult(outcome.Plan.Project, group, outcome.Plan.Details!["status"], true, outcome.Execution, outcome.Recovered,
                (object?)data?.Handle ?? _pages.Shape(data?.ResultJson, query) ?? (object?)data?.Locator ?? ApiTrySettingsResult.Text(group, parameters, data?.Value) ?? ResultShaper.CleanText(data?.Value), Hash(data?.Before), Hash(data?.After), changed.Take(20).Select(k => new ApiTryChange(ResultShaper.CleanText(k)!, ResultShaper.CleanText(data!.Before.GetValueOrDefault(k)), ResultShaper.CleanText(data!.After.GetValueOrDefault(k)))).ToArray(), changed.Length, changed.Length > 20,
                "Unverified; snapshots omit unplaced objects and 3D connections. Equal observations do not prove absence of side effects. Property changes contain JSON-encoded scalar text. Writes use one undo step; live undo verification is pending. Handles are valid only in this Host session and EPLAN process, expire after 10 idle minutes, and may be evicted at 500. Recovered calls have no inferred readback. " + data?.Warning, data?.Files ?? [], data?.UndoExpected));
        }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return ToolResults.Error(ex.Error, ex.Error.Reason == ErrorReason.InvalidArgument ? ApiCallTemplates.Closest(index, group, parameters) : null); }
    }
    internal static bool MethodParameters(string group, JsonElement parameters)
        => EntryParameters(group, parameters) && ApiTrySettingsValidation.Valid(group, n => parameters.TryGetProperty(n, out JsonElement value) ? value.ToString() : null);
    internal static bool ValidValue(string[]? shape, JsonElement value) => shape?[0] == "array"
        ? value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= 200 && value.EnumerateArray().All(v => ValidValue(shape.Skip(1).ToArray(), v))
        : ApiTryCatalog.ValidValue(shape, Kind(value), value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText());
    private static string Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => Handle(value) ? "handle" : Locator(value) ? "locator" : value.EnumerateObject().Count() == 2 && value.TryGetProperty("x", out JsonElement x) && value.TryGetProperty("y", out JsonElement y) && x.ValueKind == JsonValueKind.Number && y.ValueKind == JsonValueKind.Number && x.TryGetDouble(out double a) && y.TryGetDouble(out double b) && double.IsFinite(a) && double.IsFinite(b) ? "PointD" : "Object",
        JsonValueKind.Number => value.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0 ? "Integer" : "Float",
        JsonValueKind.True or JsonValueKind.False => "Boolean",
        _ => value.ValueKind.ToString(),
    };
    private static bool Handle(JsonElement value) => value.EnumerateObject().Count() == 2 && value.TryGetProperty("handle", out JsonElement handle) && handle.ValueKind == JsonValueKind.String && value.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String && ApiObjectHandle.Valid(handle.GetString(), type.GetString());
    private static bool Locator(JsonElement value) => value.EnumerateObject().Count() == 5
        && value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(_locatorFields, StringComparer.Ordinal)
        && value.GetProperty("project").ValueKind == JsonValueKind.String && value.GetProperty("page").ValueKind == JsonValueKind.String
        && value.GetProperty("type").ValueKind == JsonValueKind.Number && value.GetProperty("type").TryGetInt32(out int type) && type > 0
        && value.GetProperty("oid").ValueKind == JsonValueKind.Number && value.GetProperty("oid").TryGetInt64(out long oid) && oid > 0
        && value.GetProperty("digest").ValueKind == JsonValueKind.String && System.Text.RegularExpressions.Regex.IsMatch(value.GetProperty("digest").GetString()!, "\\A[0-9a-f]{64}\\z");
}
internal sealed record ApiTryChange(string Key, string? Before, string? After);
internal sealed record ApiTryResult(string Project, string Group, string Status, bool Unverified, string Execution, bool Recovered, object? Value, string? Before, string? After, IReadOnlyList<ApiTryChange> Changes, int TotalChanges, bool Truncated, string Hint, IReadOnlyList<ApiTryFile> Files, bool? UndoExpected);
