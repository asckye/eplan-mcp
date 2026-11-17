using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class RuleTools
{
    public const string CheckTool = "eplan_rule_check";
    [McpServerTool(Name = CheckTool, Title = "Check cabinet rules", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Checks locally promoted locked rules in a cabinet scope; returns pass/fail/unknown/not_applicable with evidence. Multiple cabinets require cabinet. fix_plan compiles declared repairs only; unknown or conflicting fixes refuse a plan. Never executes. Example: {\"project\":\"Line1\",\"scope\":{}}.")]
    public static async Task<CallToolResult> CheckAsync(IBridgeClient bridge, PackLibrary packs, KnowledgeReviewService reviews,
        PlanStore store, PlanTrust trust, TaskRegistry sessions, McpServer server, string project, JsonElement scope,
        string? cabinet = null, string[]? rules = null, bool fix_plan = false, [Range(1, 200)] int limit = 50,
        string? cursor = null, [Range(1, 20000)] int max_tokens = 5000,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        try
        { using JsonDocument checkedScope = JsonDocument.Parse(scope.GetRawText(), new JsonDocumentOptions { AllowDuplicateProperties = false }); }
        catch (JsonException) { return WorkflowResults.BridgeError(ToolResults.InvalidArgumentError(["/scope: use unique members."])); }
        ContextTools.CheckScope(scope, violations);
        if (string.IsNullOrWhiteSpace(project) || limit is < 1 or > 200 || max_tokens is < 1 or > 20000 || fix_plan && cursor is not null)
        { violations.Add("/project must be explicit; /limit: 1-200; /max_tokens: 1-20000; omit /cursor for fix_plan."); }
        if (rules is not null && (rules.Length is < 1 or > 500 || rules.Any(string.IsNullOrWhiteSpace) || rules.Distinct(StringComparer.Ordinal).Count() != rules.Length))
        { violations.Add("/rules: supply distinct exact locked rule IDs or omit the selector."); }
        if (violations.Count > 0)
        { return WorkflowResults.BridgeError(ToolResults.InvalidArgumentError(violations)); }
        try
        {
            PlanOwner? owner = fix_plan ? sessions.OwnerFor(server) : null;
            using BridgeProgress? reporting = BridgeProgress.Start(CallProgress.For(progress), "Checking cabinet rules", packs.Clock, cancellationToken);
            PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(project, cancellationToken).ConfigureAwait(false);
            PlanSnapshot snapshot = await new PlanBaselineReader(bridge).SnapshotAsync(project, null, cancellationToken).ConfigureAwait(false);
            if (snapshot.Project.Path != PlanSnapshotHashes.ProjectKey(names.ProjectPath))
            { throw PlanLocalScopeRules.Stale("Project identity changed; check again."); }
            ProjectBinding binding = await ContextBuilder.ReadBindingAsync(packs, snapshot.Project.Path, cancellationToken).ConfigureAwait(false)
                ?? new() { BindingVersion = 2, Project = snapshot.Project.Path, Revision = 0, Cabinets = [ContextBuilder.VirtualCabinet], ProjectValues = new(), PackRoots = [] };
            if (binding.BindingVersion != 2)
            { throw ContextBuilder.Missing("rules:v2_binding"); }
            WorkflowEngineering engineering = await WorkflowEngineering.ReadAsync(packs, binding, cabinet, null, default, cancellationToken).ConfigureAwait(false);
            var baseline = new PlanBaseline(snapshot, [.. names.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name))], names.NamesHash!);
            string[] Strings(string key) => scope.TryGetProperty(key, out JsonElement value) ? value.Deserialize<string[]>()! : [];
            var selected = new PlanScope([.. scope.TryGetProperty("pages", out _) ? Strings("pages") : snapshot.Pages.Select(p => p.Name)], [.. Strings("structures")],
                scope.TryGetProperty("locators", out JsonElement locators) ? locators.Deserialize<System.Collections.Immutable.ImmutableArray<PlanLocator>>(HostJson.Options) : []);
            if (!PlanScopeRules.Missing(baseline, selected).IsEmpty)
            { return WorkflowResults.BridgeError(ToolResults.InvalidArgumentError(["/scope: a page or locator is absent or stale; use a fresh snapshot."])); }
            AssetEntry[] official = [.. engineering.Assets.Where(a => a.Document?.Kind == KnowledgeKind.Rule && a.Standing == KnowledgeStanding.Official)];
            if (rules?.Except(official.Select(a => a.Id), StringComparer.Ordinal).Any() == true)
            { throw ContextBuilder.CabinetFailure(ErrorReason.ResourceNotFound, "/rules", "Select exact promoted rule IDs from this cabinet's locks."); }
            PlanEngineering evaluation = await engineering.CompleteAsync(reviews, baseline, cancellationToken, query: true).ConfigureAwait(false);
            var objects = snapshot.Pages.SelectMany(p => p.Objects).ToDictionary(o => o.Hash, StringComparer.Ordinal);
            RuleResult[] results = [.. evaluation.Acceptance.GetProperty("rules").Deserialize<RuleResult[]>(PlanJson.Options)!
                .Where(r => (rules is null || rules.Contains(r.Rule, StringComparer.Ordinal)) && (r.Locator.Length == 0 || objects.TryGetValue(r.Locator, out SnapshotObject? obj)
                    && PlanScopeRules.Contains(selected, PlanScopeRules.Locator(snapshot.Project.Path, obj)) && (!scope.TryGetProperty("kinds", out _) || Strings("kinds").Contains(obj.Kind, StringComparer.Ordinal))))
                .OrderBy(r => r.Rule, StringComparer.Ordinal).ThenBy(r => r.Locator, StringComparer.Ordinal)];
            var rows = new JsonArray(results.Select(r => (JsonNode)ContextBuilder.Node(new { r.Rule, r.Version, Locator = objects.TryGetValue(r.Locator, out SnapshotObject? obj) ? PlanScopeRules.Locator(snapshot.Project.Path, obj) : null, ObjectHash = r.Locator, Status = JsonSerializer.SerializeToElement(r.Status, PlanJson.Options), r.Evidence, r.Basis })).ToArray());
            string fingerprint = PageCursor.Fingerprint(CheckTool, snapshot.ComposedHash, names.NamesHash, Convert.ToBase64String(PackJson.Serialize(binding)), scope.GetRawText(), JsonSerializer.Serialize(rules), JsonSerializer.Serialize(evaluation.RuleDigests));
            int offset = PageCursor.Offset(cursor, fingerprint, CheckTool, violations);
            if (violations.Count > 0 || offset > rows.Count)
            { return WorkflowResults.BridgeError(ToolResults.InvalidArgumentError(["/cursor: invalid or stale; omit it to restart."])); }
            JsonObject data = Page(ContextBuilder.Node(new { Project = snapshot.Project.Path, Cabinet = engineering.Cabinet.Id, ContextRef = ContextBuilder.Reference(snapshot.ComposedHash, binding), Coverage = evaluation.Acceptance.GetProperty("uncovered"), PlanId = (string?)null }), rows, evaluation.RuleDigests, offset, limit, max_tokens, fingerprint);
            if (fix_plan && results.Any(r => r.Status == RuleStatus.Unknown || r.Basis != "fact"))
            { throw ContextBuilder.Missing("rules:known_fix_evidence"); }
            if (fix_plan && results.Any(r => r.Status == RuleStatus.Fail))
            {
                var steps = new List<PlanArgumentStep>();
                var fixes = new JsonArray();
                var targets = new HashSet<string>(StringComparer.Ordinal);
                RuleResult[] repairs = [.. results.Where(r => r.Status == RuleStatus.Fail && official.Single(a => a.Id == r.Rule).Document!.Spec.ContainsKey("fix"))];
                foreach (RuleResult repair in repairs)
                {
                    AssetEntry rule = official.Single(a => a.Id == repair.Rule);
                    JsonObject fix = rule.Document!.Spec["fix"]!.AsObject();
                    AssetEntry workflow = engineering.Assets.SingleOrDefault(a => a.Document is { Kind: KnowledgeKind.Workflow, ApiVersion: KnowledgeDocument.V2ApiVersion } && (a.Id == (string?)fix["workflow"] || a.Pack == rule.Pack && a.Local == (string?)fix["workflow"]))
                        ?? throw ContextBuilder.CabinetFailure(ErrorReason.ResourceNotFound, "/fix/workflow", "The declared fix must reference a locked workflow.");
                    var inputs = new JsonObject();
                    foreach (var input in fix["inputs"]!.AsObject())
                    {
                        JsonObject node = input.Value!.AsObject();
                        JsonNode? value = node["object"] is not null ? JsonValue.Create(objects[repair.Locator].Name) : node["literal"]?.DeepClone()
                            ?? (node["config"] is { } key ? engineering.Config.SingleOrDefault(c => (string?)c!["key"] == key.GetValue<string>())?["value"]?.DeepClone()
                            : node["property"] is { } property ? JsonSerializer.SerializeToNode(new WorkflowEngineering.Facts(snapshot, engineering.Cabinet).GetProperty(repair.Locator, property.GetValue<string>())?.Value) : null);
                        if (value is null || node.ContainsKey("unit"))
                        { throw ContextBuilder.Missing("rules:fix_input_coverage"); }
                        inputs[input.Key] = value;
                    }
                    var fixEngineering = engineering with { Workflow = workflow, Inputs = JsonSerializer.SerializeToElement(inputs) };
                    PlanArgumentCheck expanded = fixEngineering.Expand(project);
                    if (!expanded.Violations.IsEmpty)
                    { return WorkflowResults.Error(ErrorReason.InvalidArgument, "Correct the declared fix arguments.", expanded.Metadata); }
                    if (steps.Count + expanded.Steps.Length > 20)
                    { throw ContextBuilder.Missing("rules:fix_step_limit"); }
                    BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                    if ((workflow.Document!.Spec["requiredCapabilities"]?.AsArray() ?? []).Select(c => c!.GetValue<string>().Replace("op:", "", StringComparison.Ordinal)).Except(status.Handshake?.Operations ?? [], StringComparer.Ordinal).Any()
                        || (await fixEngineering.CompleteAsync(reviews, baseline, cancellationToken, query: true).ConfigureAwait(false)).Acceptance.GetProperty("rules").Deserialize<RuleResult[]>(PlanJson.Options)!.Any(r => (r.Rule == workflow.Id || workflow.Document.Spec["acceptance"]?.AsArray().Any(a => (string?)a == r.Rule || (string?)a == official.SingleOrDefault(e => e.Id == r.Rule && e.Pack == workflow.Pack)?.Local) == true) && (r.Status == RuleStatus.Unknown || r.Status == RuleStatus.Fail && !repairs.Any(p => p.Rule == r.Rule && p.Locator == r.Locator))))
                    { throw ContextBuilder.Missing("rules:fix_workflow_applicability"); }
                    fixes.Add(ContextBuilder.Node(new { rule = rule.Id, workflow = workflow.Id, inputs }));
                    string[] affected = [.. expanded.Steps.SelectMany(PlanDependencyPredictor.Targets).Distinct(StringComparer.Ordinal)];
                    if (affected.Length == 0 || affected.Any(targets.Contains))
                    { throw ContextBuilder.CabinetFailure(ErrorReason.ConfigConflict, "/fix", "Fix scopes overlap or cannot be proved disjoint; request separate plans.", fixes.ToJsonString()); }
                    targets.UnionWith(affected);
                    int start = steps.Count;
                    steps.AddRange(expanded.Steps.Select(s => s with { Index = s.Index + start, DependsOn = [.. s.DependsOn.Select(d => d + start)] }));
                }
                if (repairs.Length > 0)
                {
                    PlanArgumentCheck input = PlanArguments.Check(JsonSerializer.SerializeToElement(new { project, steps = steps.Select(s => new { s.Op, s.Args, depends_on = s.DependsOn }) }, PlanJson.Options), null, "");
                    if (!input.Violations.IsEmpty)
                    { return WorkflowResults.Error(ErrorReason.InvalidArgument, "Correct the combined fix arguments.", input.Metadata); }
                    var (definition, evidence) = await WorkflowCompilation.CompileAsync(bridge, packs, input, project, owner!, reviews, cabinet, null, JsonSerializer.SerializeToElement(new { fixes }), cancellationToken, repairs).ConfigureAwait(false);
                    if (definition.BindingRevision != binding.Revision || !definition.Resources.SequenceEqual(engineering.Resources))
                    { throw PlanLocalScopeRules.Stale("Rule-check inputs changed; check again before planning repairs."); }
                    string changes = PlanDiffPreview.For(definition.Steps, local: true);
                    definition = definition with { Digests = definition.Digests with { ChangesDigest = PlanStore.Hash(Encoding.UTF8.GetBytes(changes)) } };
                    PlanRecord record = await store.PublishAsync(definition, changes, evidence, cancellationToken).ConfigureAwait(false);
                    _ = trust.Register(record.Definition);
                    data["planId"] = definition.PlanId;
                }
            }
            return ToolResults.Success("Cabinet rules checked; no project data changed.", ContextBuilder.Clean(data));
        }
        catch (BridgeCallException ex) { return WorkflowResults.BridgeError(ex.Error); }
        catch (WorkflowCompilationException ex) { return WorkflowResults.Error(ex.Reason, ex.Message, ex.Metadata); }
        catch (TaskRegistryException ex) { return WorkflowResults.Error(ex.Error.Reason, ex.Error.Message, ex.Error.Metadata); }
        catch (PlanCapacityException ex) { return WorkflowResults.Error(ex.Reason, ex.Message, ex.Metadata); }
        catch (PackFormatException ex) { return WorkflowResults.Error(ErrorReason.PackInvalid, ResultShaper.CleanText(ex.Message)); }
        catch (AuditUnavailableException) { return WorkflowResults.Error(ErrorReason.AuditUnavailable, "Verify the audit log before checking rules again."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return WorkflowResults.Error(ErrorReason.Internal, "Check local pack and plan storage before retrying."); }
    }

    internal static JsonObject Page(JsonObject data, JsonArray rows, JsonElement sources, int offset, int limit, int budget, string fingerprint)
    {
        var page = new JsonArray(rows.Skip(offset).Take(limit).Select(r => r!.DeepClone()).ToArray());
        while (true)
        {
            data["rules"] = page.DeepClone();
            if (data["coverage"] is JsonArray coverage)
            { data["coverage"] = new JsonArray(coverage.Where(c => page.Any(r => (string?)r!["rule"] == (string?)c)).Select(c => c!.DeepClone()).ToArray()); }
            data["sources"] = JsonSerializer.SerializeToNode(page.Select(r => (string)r!["rule"]!).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => sources.GetProperty(id)));
            data["total"] = rows.Count;
            data["nextCursor"] = offset + page.Count < rows.Count ? PageCursor.Encode(offset + page.Count, fingerprint) : null;
            data["truncated"] = offset + page.Count < rows.Count;
            data["budget"] = budget;
            if (ResultShaper.ResultTokens(ContextBuilder.Clean(data).ToJsonString(HostJson.Options)) + 100 <= budget)
            { return data; }
            if (page.Count <= 1)
            { throw ContextBuilder.CabinetFailure(ErrorReason.InvalidArgument, "/max_tokens", "One result or the envelope exceeds the budget; increase max_tokens."); }
            page.RemoveAt(page.Count - 1);
        }
    }

    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        McpServerTool tool = tools.Single(t => t.ProtocolTool.Name == CheckTool);
        JsonObject schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!.AsObject();
        schema["properties"]!["scope"] = JsonNode.Parse(tools.Single(t => t.ProtocolTool.Name == ContextTools.GetTool).ProtocolTool.InputSchema.GetProperty("properties").GetProperty("scope").GetRawText());
        schema["properties"]!["rules"] = JsonNode.Parse("""{"type":"array","minItems":1,"maxItems":500,"uniqueItems":true,"items":{"type":"string","minLength":1}}""");
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
        tool.ProtocolTool.OutputSchema = JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["project","cabinet","contextRef","coverage","planId","rules","sources","total","nextCursor","truncated","budget"],"properties":{"project":{"type":"string"},"cabinet":{"type":"string"},"contextRef":{"type":"object"},"coverage":{"type":"array","items":{"type":"string"}},"planId":{"type":["string","null"]},"rules":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["rule","version","locator","objectHash","status","evidence","basis"],"properties":{"rule":{"type":"string"},"version":{"type":"string"},"locator":{"type":["object","null"]},"objectHash":{"type":"string"},"status":{"type":"string","enum":["pass","fail","unknown","not_applicable"]},"evidence":{"type":"array","items":{"type":"object"}},"basis":{"type":"string","enum":["fact","inference"]}}}},"sources":{"type":"object"},"total":{"type":"integer"},"nextCursor":{"type":["string","null"]},"truncated":{"type":"boolean"},"budget":{"type":"integer"}}}""").RootElement.Clone();
    }
}
