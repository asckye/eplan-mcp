using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Host.Tools;

/// <summary>Exact cabinet inputs and conservative coverage; no scope is shortened to obtain approval.</summary>
internal sealed record WorkflowEngineering(CabinetBinding Cabinet, JsonArray Config, AssetEntry[] Assets, PlanResource[] Resources,
    JsonElement Schema, AssetEntry? Workflow, JsonElement Inputs)
{
    public static async Task<WorkflowEngineering> ReadAsync(PackLibrary packs, ProjectBinding binding, string? cabinet, string? workflow, JsonElement inputs, CancellationToken cancellationToken)
    {
        CabinetBinding selected = ContextBuilder.SelectCabinet(binding, cabinet);
        (JsonArray config, IReadOnlyList<PackReference> pins) = await ContextBuilder.CabinetConfigAsync(packs, binding, selected, cancellationToken).ConfigureAwait(false);
        var assets = new List<AssetEntry>();
        var resources = new List<PlanResource>();
        var items = new JsonObject();
        foreach (PackReference pin in pins)
        {
            string folder = new PlanPaths(packs.Locations.Root).Check(packs.Installed.FolderOf(pin.Id, pin.Version));
            var (manifest, digest, problems) = await PackVerifier.ReadManifestAsync(folder, cancellationToken).ConfigureAwait(false);
            if (manifest is null || problems.Count > 0 || digest != pin.Digest || manifest.Id != pin.Id || manifest.Version != pin.Version)
            { throw ContextBuilder.CabinetFailure(ErrorReason.PackInvalid, "/locks", "The locked manifest changed; check the pack and compile again."); }
            foreach (AssetEntry entry in await AssetIndex.ReadAsync(folder, manifest, cancellationToken).ConfigureAwait(false))
            {
                if (entry.DocumentProblem is not null || await PackVerifier.ResourceProblemAsync(folder, entry.Resource, packs.Config.PackSources, cancellationToken).ConfigureAwait(false) is not null)
                { throw ContextBuilder.CabinetFailure(ErrorReason.PackInvalid, "/locks", "Locked resource content changed; check the pack and compile again."); }
                assets.Add(entry);
                resources.Add(new(pin.Id, pin.Version, entry.Resource.Path, pin.Digest, entry.Resource.Sha256));
                if (entry.Document?.Kind == KnowledgeKind.ConfigSchema)
                {
                    foreach (var item in entry.Document.Spec["items"]!.AsObject())
                    { items.TryAdd(item.Key, item.Value!.DeepClone()); }
                }
            }
        }
        AssetEntry? template = workflow is null ? null : assets.SingleOrDefault(a => a.Id == workflow && a.Document is { Kind: KnowledgeKind.Workflow, ApiVersion: KnowledgeDocument.V2ApiVersion })
            ?? throw ContextBuilder.CabinetFailure(ErrorReason.ResourceNotFound, "/workflow", "Use an exact workflow from this cabinet's locked packs.");
        return new(selected, config, [.. assets], [.. resources], JsonSerializer.SerializeToElement(new { items }), template,
            inputs.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement(new { }) : inputs);
    }

    public PlanArgumentCheck Expand(string project)
    {
        AssetEntry Resolve(string id) => Assets.SingleOrDefault(a => a.Id == id || a.Pack == Workflow!.Pack && a.Local == id)
            ?? throw ContextBuilder.CabinetFailure(ErrorReason.ResourceNotFound, "/spec", "A workflow reference is outside the exact locked resource closure.");
        WorkflowDefinition definition = WorkflowValidator.Validate(JsonSerializer.SerializeToElement(Workflow!.Document!.Spec), Schema, (id, kind) => Resolve(id).Resource.Kind == kind);
        ImmutableArray<WorkflowStep> expanded = WorkflowValidator.Expand(definition, Inputs, JsonSerializer.SerializeToElement(Config.ToDictionary(v => (string)v!["key"]!, v => v!["value"])));
        if (definition.Spec.TryGetProperty("acceptance", out JsonElement acceptance) && acceptance.EnumerateArray().Any(a => Resolve(a.GetString()!).Resource.Kind == ResourceKind.Sample))
        { throw PlanLocalScopeRules.Missing("workflow:acceptance_samples"); }
        return PlanArguments.Check(JsonSerializer.SerializeToElement(new { project, steps = expanded.Select(s => new { s.Op, s.Args, depends_on = s.DependsOn }) }, PlanJson.Options), null, "");
    }

    internal void Guard(ProjectBinding binding, PlanBaseline baseline, IEnumerable<PlanStep> steps)
    {
        var resolver = new CabinetScopeResolver(binding.Cabinets.Count > 0 ? binding.Cabinets : [Cabinet]);
        void Require(string? name)
        {
            if (!resolver.CabinetOf(name).SequenceEqual([Cabinet.Id]))
            { throw ContextBuilder.CabinetFailure(ErrorReason.InvalidArgument, "/cabinet", "The step affects an unassigned or different cabinet; select a contained scope and compile again."); }
        }
        foreach (PlanStep step in steps)
        {
            if (step.Op is "macro.insert" or "page.renumber" or "device.renumber")
            { throw PlanLocalScopeRules.Missing("cabinet:complete_predicted_impact"); }
            foreach (PlanScope scope in new[] { step.ReadScope, step.ModifyScope })
            {
                foreach (string page in scope.Pages)
                { Require(page); }
                foreach (PlanLocator locator in scope.Locators)
                { Require(locator.Kind == "connection" ? locator.Page : locator.Name); }
                foreach (SnapshotObject obj in PlanScopeRules.Select(baseline.Snapshot, scope))
                {
                    if (obj.Kind == "connection")
                    { Require(obj.Start?.Function); Require(obj.End?.Function); }
                    else
                    { Require(obj.Name); }
                }
            }
            foreach (PlanAbsent absent in step.Preconditions.OfType<PlanAbsent>())
            { Require(absent.Name); }
            foreach (PlanChange change in step.PlannedChanges.Where(c => c.Property == "tag"))
            { Require(change.NewValue); }
        }
    }

    public async Task<PlanEngineering> CompleteAsync(KnowledgeReviewService reviews, PlanBaseline baseline, CancellationToken cancellationToken)
    {
        var values = Config.Deserialize<ImmutableArray<PlanConfigValue>>(PlanJson.Options);
        var config = values.ToDictionary(v => v.Key, v => new RuleValue(new(v.Value.ValueKind == JsonValueKind.String ? "text" : v.Value.ValueKind == JsonValueKind.Number ? "number" : "boolean", v.Unit), v.Value), StringComparer.Ordinal);
        RuleValueType? TypeOf(JsonElement items, string key) => items.TryGetProperty(key, out JsonElement item) ? new(item.GetProperty("type").GetString() == "choice" ? "text" : item.GetProperty("type").GetString()!, item.TryGetProperty("unit", out JsonElement unit) ? unit.GetString() : null) : null;
        var parameters = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        foreach (var p in Workflow?.Document?.Spec["parameters"]?.AsObject() ?? [])
        {
            JsonElement type = JsonSerializer.SerializeToElement(p.Value);
            if (Inputs.TryGetProperty(p.Key, out JsonElement supplied) || type.TryGetProperty("default", out supplied))
            { parameters[p.Key] = new(TypeOf(JsonSerializer.SerializeToElement(Workflow!.Document!.Spec["parameters"]), p.Key)!.Value, supplied); }
        }
        var facts = new Facts(baseline.Snapshot);
        var digests = new JsonObject();
        var rules = new List<RuleDefinition>();
        var mandatoryRules = new HashSet<string>(StringComparer.Ordinal);
        var uncovered = new List<string>();
        foreach (AssetEntry entry in Assets.Where(a => a.Document?.Kind == KnowledgeKind.Rule))
        {
            string confirmation = await reviews.CheckPlanAsync(entry, Assets, cancellationToken).ConfigureAwait(false);
            JsonElement spec = JsonSerializer.SerializeToElement(entry.Document!.Spec);
            (RuleNode scope, RuleNode check) = RuleValidator.Validate(spec, key => TypeOf(Schema.GetProperty("items"), key));
            bool mandatory = spec.GetProperty("severity").GetString() == "mandatory" || Workflow?.Document?.Spec["acceptance"]?.AsArray().Any(a => (string?)a == entry.Id || entry.Pack == Workflow.Pack && (string?)a == entry.Local) == true;
            string kind = spec.GetProperty("scope").GetProperty("kind").GetString()!;
            bool incomplete = baseline.Names.Any(n => (n.Kind == kind || n.Kind == "page") && new CabinetScopeResolver([Cabinet]).CabinetOf(n.Name).Count > 0 && !facts.Has(n.Kind, n.Name)) || kind == "connection" && baseline.Snapshot.SkippedConnections > 0;
            if (incomplete)
            { uncovered.Add(entry.Id); }
            if (mandatory && incomplete)
            { throw new WorkflowCompilationException(ErrorReason.CapabilityMissing, "A mandatory rule lacks complete cabinet evidence; resolve it before compiling.", new { pointer = "/rules", missing = new[] { entry.Id } }); }
            digests[entry.Id] = JsonSerializer.SerializeToNode(new { sha256 = entry.Resource.Sha256, reviewDigest = confirmation });
            rules.Add(new(entry.Id, entry.Resource.Sha256, kind, scope, check));
            if (mandatory)
            { mandatoryRules.Add(entry.Id); }
        }
        if (Workflow?.Document?.Spec["applicability"] is { } predicate)
        {
            // Validate with a registered kind; the singleton evaluation does not claim project object coverage.
            var validated = RuleValidator.Validate(JsonSerializer.SerializeToElement(new { severity = "mandatory", source = "workflow", scope = new { kind = "page", where = new { literal = true } }, check = predicate, message = new { en = "Applicability" } }), key => TypeOf(Schema.GetProperty("items"), key), key => TypeOf(JsonSerializer.SerializeToElement(Workflow.Document.Spec["parameters"]), key));
            rules.Add(new(Workflow.Id, Workflow.Resource.Sha256, "workflow", validated.Scope, validated.Check));
            mandatoryRules.Add(Workflow.Id);
        }
        if (rules.Count > RuleInterpreter.MaxRules)
        { throw PlanLocalScopeRules.Missing("rules:cabinet_limit"); }
        IReadOnlyList<RuleResult> results = await new RuleInterpreter().EvaluateAsync(rules, facts, config, parameters, cancellationToken).ConfigureAwait(false);
        if (results.Any(r => mandatoryRules.Contains(r.Rule) && r.Status is RuleStatus.Fail or RuleStatus.Unknown))
        { throw new WorkflowCompilationException(ErrorReason.CapabilityMissing, "A mandatory rule or applicability failed or is unknown; resolve its evidence before compiling.", new { pointer = "/rules", missing = results.Where(r => mandatoryRules.Contains(r.Rule) && r.Status is RuleStatus.Fail or RuleStatus.Unknown).Select(r => r.Rule).Distinct(StringComparer.Ordinal) }); }
        return new(Cabinet.Id, values, PlanStore.Hash(System.Text.Encoding.UTF8.GetBytes(PlanCanonical.Serialize(JsonSerializer.SerializeToElement(Config)))),
            JsonSerializer.SerializeToElement(new { workflow = Workflow?.Id, inputs = Inputs }), [], JsonSerializer.SerializeToElement(digests),
            JsonSerializer.SerializeToElement(new { cabinetScope = Cabinet.Structure, rules = results, uncovered }, PlanJson.Options));
    }

    private sealed class Facts(PlanSnapshot snapshot) : IRuleFactSnapshot
    {
        private readonly Dictionary<string, SnapshotObject> _objects = snapshot.Pages.SelectMany(p => p.Objects).ToDictionary(o => o.Hash, StringComparer.Ordinal);
        private readonly HashSet<(string Kind, string? Name)> _names = [.. snapshot.Pages.SelectMany(p => p.Objects).Select(o => (o.Kind, o.Name))];
        public bool Has(string kind, string name) => _names.Contains((kind, name));
        public IReadOnlyList<string> Objects(string kind) => kind == "workflow" ? ["workflow"] : [.. _objects.Where(o => o.Value.Kind == kind).Select(o => o.Key)];
        public RuleValue? GetProperty(string locator, string name)
        {
            SnapshotObject? obj = _objects.GetValueOrDefault(locator);
            object? value = name switch { "page.name" or "function.name" or "connection.name" => obj?.Name, "page.type" => obj?.PageType, "function.category" => obj?.Category, "function.main" => obj?.Main, _ => null };
            return value is null ? null : new(RuleValidator.Properties[name].Type, JsonSerializer.SerializeToElement(value));
        }
        public RuleRelation Relation(string locator, string relation) => new([], false, "Local snapshot does not prove complete endpoint relations.");
    }
}
