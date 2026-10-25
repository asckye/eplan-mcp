using System.Diagnostics;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One read inside the dispatcher's main-thread LockingStep (ADR-0003, A32/A36); no transaction or cached evidence.</summary>
public sealed class PlanStepGuardOperation(IProjectCatalog catalog, ISnapshotReader reader, IObjectFinder finder,
    IPlanFunctionReader functions, Func<long>? milliseconds = null) : Operation<PlanStepGuardArgs, string>
{
    public override string Name => OperationNames.PlanStepGuard;

    public override object ParseArgs(JToken? args)
    {
        object parsed = base.ParseArgs(args);
        bool Known(JToken input, JToken shape) => input is JObject obj
            ? obj.Properties().All(p => shape[p.Name] is { } field && Known(p.Value, field))
            : input is not JArray array || array.Select((item, i) => Known(item, shape[i]!)).All(known => known);
        if (args is not null && !Known(args, PlanGuardScopeRules.Canonical(parsed)))
        {
            throw Invalid("Unknown guard fields are not allowed.");
        }

        return parsed;
    }

    protected override string Execute(PlanStepGuardArgs args)
    {
        var clock = Stopwatch.StartNew();
        Func<long> now = milliseconds ?? (() => clock.ElapsedMilliseconds);
        long start = now();
        void CheckTime()
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (now() - start >= 10_000)
            {
                throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.Timeout,
                    "The step guard exceeded 10 s; recompile the plan before writing.", retryable: true));
            }
        }

        Validate(args);
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        CheckTime();
        using ISnapshotSource source = reader.Open(project.Path);
        PlanGuardScope[] scopes = [args.ReadScope, .. args.Preconditions.Where(p => p.Scope is not null).Select(p => p.Scope!)];
        ObjectLocator[] locators = [.. scopes.SelectMany(s => s.Locators).Concat(args.Preconditions.Where(p => p.Locator is not null).Select(p => p.Locator!))];
        // A read scope may name every page of the project (collision domains, design 4.3); the 10,000-object bound below
        // limits it. The 50-page limit applies to modify scopes, which the Host checks when it compiles the plan.
        var pages = source.Pages.Where(p => scopes.Any(s => PlanGuardScopeRules.Page(s, p.Value))
            || locators.Any(l => (l.Kind == "page" ? l.Name : l.Page) == p.Value)).ToArray();

        var objects = new List<SnapshotObject>();
        var hashes = new Dictionary<long, string>();
        IReadOnlyDictionary<int, SnapshotProperty> registered = SnapshotCanonical.Registered(SnapshotProfiles.StandardName)!;
        foreach (var page in pages)
        {
            CheckTime();
            IReadOnlyList<SnapshotSourceObject> raw = source.ReadPage(page.Key, SnapshotProfiles.StandardName);
            CheckTime();
            if (objects.Count + raw.Count > 10_000)
            {
                throw Invalid("A guard may cover at most 10,000 objects, including pages.");
            }

            SnapshotPage read = SnapshotCanonical.Page(SnapshotProfiles.StandardName, raw[0], raw.Skip(1), registered);
            objects.AddRange(read.Objects);
            hashes.Add(read.Oid, read.Hash);
        }

        var conflicts = new JArray();
        void Conflict(object target, bool locator, string reason, object? expected, object? actual) => conflicts.Add(new JObject
        {
            [locator ? "locator" : "scope"] = PlanGuardScopeRules.Canonical(target),
            ["reason"] = reason,
            ["expected"] = expected is null ? JValue.CreateNull() : PlanGuardScopeRules.Canonical(expected),
            ["actual"] = actual is null ? JValue.CreateNull() : PlanGuardScopeRules.Canonical(actual),
        });
        var indexed = objects.ToDictionary(o => (o.Kind, o.Type, o.Oid));
        ObjectLocator[] missing = [.. locators.Where(l => !indexed.ContainsKey((l.Kind, l.Type, l.Oid)))
            .GroupBy(l => (l.Kind, l.Type, l.Oid)).Select(g => g.First())];
        CheckTime();
        if (missing.Length > 0)
        {
            foreach (SnapshotSourceObject raw in finder.Find(project.Path, missing, SnapshotProfiles.StandardName).OfType<SnapshotSourceObject>())
            {
                CheckTime();
                SnapshotObject obj = SnapshotCanonical.ObjectOf(raw, registered);
                indexed[(obj.Kind, obj.Type, obj.Oid)] = obj;
            }

            objects = [.. indexed.Values];
        }

        if (objects.Count > 10_000)
        {
            throw Invalid("A guard may cover at most 10,000 objects, including pages.");
        }

        CheckTime();
        SnapshotObject? Find(ObjectLocator locator) => indexed.TryGetValue((locator.Kind, locator.Type, locator.Oid), out SnapshotObject? found)
            ? found : objects.FirstOrDefault(o => o.Kind == locator.Kind && o.Oid == locator.Oid);
        foreach (ObjectLocator locator in scopes.SelectMany(s => s.Locators))
        {
            LocatorResult check = LocatorCheck.Check(0, locator, project.Path, Find(locator));
            if (check.Status != LocatorResult.Resolved)
            {
                Conflict(locator, true, check.Reason!, locator, check);
            }
        }

        IReadOnlyList<PlanFunction> names = args.Preconditions.Any(p => p.Tag == "absent" && p.Kind == "function") ? functions.Read(project.Path) : [];
        CheckTime();
        if (objects.Count + names.Select(f => (f.Kind, f.Type, f.Oid)).Distinct().Count(k => !indexed.ContainsKey(k)) > 10_000)
        {
            throw Invalid("A guard's name domain may cover at most 10,000 functions.");
        }

        foreach (PlanGuardCondition condition in args.Preconditions)
        {
            CheckTime();
            object? expected = condition.Hash, actual = null;
            if (condition.Locator is { } locator)
            {
                LocatorResult check = LocatorCheck.Check(0, locator, project.Path, Find(locator));
                actual = condition.Tag == "pageHash" ? hashes.TryGetValue(locator.Oid, out string? hash) ? hash : null : check.Hash;
                if (check.Status != LocatorResult.Resolved)
                {
                    Conflict(locator, true, check.Reason!, locator, check);
                    continue;
                }
            }
            else if (condition.Scope is { } scope)
            {
                SnapshotObject[] selected = [.. objects.Where(o => PlanGuardScopeRules.Contains(scope, o))];
                expected = new { hash = condition.Hash, count = condition.Count };
                actual = new { hash = PlanGuardScopeRules.Members(project.Path, selected), count = selected.Length };
            }
            else if (condition.Tag == "absent")
            {
                expected = false;
                IEnumerable<string> occupied = condition.Kind == "page" ? source.Pages.Select(p => p.Value) : names.Select(f => f.Name);
                actual = occupied.Any(n => string.Equals(n, condition.Name, StringComparison.OrdinalIgnoreCase)
                    || (condition.Kind == "function" && n.StartsWith(condition.Name + ":", StringComparison.OrdinalIgnoreCase)));
            }
            else if (condition.Tag == "resource")
            {
                expected = condition.Sha256;
                actual = args.ResourceHashes.Contains(condition.Sha256!, StringComparer.Ordinal) ? condition.Sha256
                    : (object)args.ResourceHashes.Distinct(StringComparer.Ordinal).OrderBy(h => h, StringComparer.Ordinal).ToArray();
            }
            else if (condition.Tag == "binding")
            {
                expected = new { revision = condition.Revision, lockDigest = condition.LockDigest };
                actual = new { revision = args.BindingRevision, lockDigest = args.LockDigest };
            }

            if (!JToken.DeepEquals(PlanGuardScopeRules.Canonical(expected!), actual is null ? JValue.CreateNull() : PlanGuardScopeRules.Canonical(actual)))
            {
                Conflict((object?)condition.Locator ?? condition.Scope ?? (object)new { condition.Kind, condition.Name, condition.Tag },
                    condition.Locator is not null, condition.Tag, expected, actual);
            }
        }

        string json = new JArray(conflicts.Select(c => PlanGuardScopeRules.Canonical(c).ToString(Formatting.None))
            .Distinct(StringComparer.Ordinal).Select(JToken.Parse)).ToString(Formatting.None);
        CheckTime();
        if (conflicts.Count > 0)
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PlanStale,
                "The step's read scope or preconditions changed; recompile the plan before writing.", retryable: false,
                new Dictionary<string, string> { ["conflicts"] = json }));
        }

        return "ok";
    }

    private static void Validate(PlanStepGuardArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Project) || args.ReadScope is null || args.Preconditions is null or { Count: > 20_300 }
            || args.ResourceHashes is null or { Count: > 200 } || args.ResourceHashes.Any(h => h is null || !Digest(h)))
        {
            throw Invalid("Supply a project, complete readScope, bounded preconditions and actual resource hashes.");
        }

        foreach (PlanGuardCondition condition in args.Preconditions)
        {
            string[] fields = condition is null ? [] : PlanGuardScopeRules.Canonical(condition).Children<JProperty>()
                .Where(p => p.Name != "tag" && p.Value.Type != JTokenType.Null).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] required = condition?.Tag switch
            {
                "objectHash" or "pageHash" => ["hash", "locator"],
                "members" => ["count", "hash", "scope"],
                "absent" => ["kind", "name"],
                "resource" => ["sha256"],
                "binding" => ["lockDigest", "revision"],
                _ => [],
            };
            if (required.Length == 0 || !fields.SequenceEqual(required, StringComparer.Ordinal) || condition!.Hash is { } hash && !Digest(hash)
                || condition!.Sha256 is { } sha && !Digest(sha) || condition!.LockDigest is { } digest && !Digest(digest)
                || condition!.Count is < 0 or > 10_000 || condition!.Revision is < 0
                || condition!.Tag == "pageHash" && condition.Locator!.Kind != "page"
                || condition!.Tag == "absent" && (condition.Kind is not ("page" or "function") || string.IsNullOrWhiteSpace(condition.Name)))
            {
                throw Invalid("Preconditions must use exactly the fields of one of the six registered tags.");
            }
        }

        PlanGuardScope[] scopes = [args.ReadScope, .. args.Preconditions.Where(p => p.Scope is not null).Select(p => p.Scope!)];
        ObjectLocator[] locators = [.. scopes.SelectMany(s => s.Locators ?? []).Concat(args.Preconditions.Where(p => p.Locator is not null).Select(p => p.Locator!))];
        if (scopes.Any(s => s.Pages is null or { Count: > 10_000 } || s.Structures is null or { Count: > 50 } || s.Locators is null
            || s.Pages.Any(string.IsNullOrWhiteSpace) || s.Structures.Any(p => p is null || !Regex.IsMatch(p, @"\A(?:[=+&#][^=+&#/\s*?]+)+\z")))
            || scopes.SelectMany(s => s.Locators).Select(l => PlanGuardScopeRules.Canonical(l).ToString(Formatting.None)).Distinct(StringComparer.Ordinal).Count() > 200
            || locators.Any(l => l is null || string.IsNullOrWhiteSpace(l.Project) || !System.IO.Path.IsPathRooted(l.Project)
                || !LocatorCheck.Kinds.Contains(l.Kind) || l.Type <= 0 || l.Oid <= 0 || l.Pos is not null
                && (LocatorCheck.Position(l.Pos) is not { } pos || double.IsNaN(pos.X) || double.IsNaN(pos.Y) || double.IsInfinity(pos.X) || double.IsInfinity(pos.Y)))
            || !args.Preconditions.Any(p => p.Tag == "members" && JToken.DeepEquals(PlanGuardScopeRules.Canonical(p.Scope!), PlanGuardScopeRules.Canonical(args.ReadScope))))
        {
            throw Invalid("Scopes require exact pages/structure segments, at most 200 explicit locators, and a members check for the complete readScope.");
        }
    }

    private static bool Digest(string value) => Regex.IsMatch(value, @"\A[0-9a-f]{64}\z");
    private static BridgeErrorException Invalid(string message) => WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.InvalidArgument,
        message, retryable: false, new Dictionary<string, string> { ["violations"] = PlanGuardScopeRules.Canonical(new[] { message }).ToString(Formatting.None) }));
}
