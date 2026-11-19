using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

public class UnplacedFunctionDeleteOperation(IProjectCatalog catalog, IUnplacedFunctionRemover remover,
    ITransactionScopeFactory transactions, bool execute, Action<OpenProject, BackupTarget?> backup) : Operation<JObject, JObject>
{
    public override string Name => execute ? OperationNames.UnplacedDeleteExecute : OperationNames.UnplacedDeletePlan;

    protected override JObject Execute(JObject args)
    {
        OpenProject project;
        NamedFunctionRow[] before;
        NamedFunctionRow[] targets;
        string[] tags;
        string fingerprint;
        Action remove;
        JObject result;
        try
        {
            JObject input = execute ? args["request"] as JObject ?? throw WriteArguments.Invalid(["Supply request."]) : args;
            tags = Validate(input);
            project = WriteArguments.Writable(catalog, (string)input["project"]!, (string?)args["projectPath"]);
            before = Read(project.Path, tags);
            fingerprint = Hash(before);
            if (execute && fingerprint != (string?)args["fingerprint"])
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The named functions changed. Preview the deletion again.");
            }
            targets = Targets(tags, before);
            result = new JObject
            {
                ["project"] = project.Name,
                ["unplaced"] = new JArray(tags),
                ["items"] = new JArray(targets.Select(row => new JObject
                {
                    ["name"] = row.Name,
                    ["type"] = row.Type,
                    ["oid"] = row.Oid,
                    ["category"] = row.Category
                })),
                ["dependents"] = before.Length - targets.Length,
                ["fingerprint"] = fingerprint
            };
            if (!execute)
            {
                return (JObject)BridgeJson.FromObject(new WritePlan
                {
                    Project = project.Name,
                    ProjectPath = project.Path,
                    Fingerprint = fingerprint,
                    Changes = targets.Select(row => Change(row, before)).ToList(),
                    Details = new() { ["preview"] = result.ToString(Formatting.None) }
                });
            }
            remove = remover.Prepare(project.Path, targets);
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        backup(project, args["backup"]?.ToObject<BackupTarget>());
        Committed<bool> committed = TransactionRunner.Run(transactions, PlacementDeleteOperation.Tool, () =>
        {
            remove();
            return true;
        }, verifyCommitted: _ => VerifyCommitted(before, targets, tags, Read(project.Path, tags)), verifyRollback: () =>
        {
            if (Hash(Read(project.Path, tags)) != fingerprint)
            {
                throw new InvalidOperationException("Deletion rollback did not restore the named functions.");
            }
        });
        result["execution"] = ExecutionState.Committed;
        result["warning"] = committed.Warning;
        return result;
    }

    private static string[] Validate(JObject input)
    {
        var errors = new List<string>();
        if (input["project"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)input["project"]))
        {
            errors.Add("/project requires a nonempty string.");
        }
        if (input["dry_run"] is { Type: not JTokenType.Boolean })
        {
            errors.Add("/dry_run must be boolean.");
        }
        foreach (JProperty property in input.Properties().Where(p => p.Name is not ("project" or "unplaced" or "dry_run")))
        {
            errors.Add($"/{property.Name} must be omitted with unplaced.");
        }
        string?[]? tags = (input["unplaced"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string?)t : null).ToArray();
        UnplacedDeleteTags.Validate(tags, errors);
        if (errors.Count > 0)
        {
            throw WriteArguments.Invalid(errors);
        }
        return tags!;
    }

    private NamedFunctionRow[] Read(string project, string[] tags) => remover.Find(project, tags)
        .OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.Oid).ThenBy(row => row.Type).ToArray();

    private static string Hash(NamedFunctionRow[] rows) => Fingerprint.Of([JsonConvert.SerializeObject(rows)]);

    private static NamedFunctionRow[] Targets(string[] tags, NamedFunctionRow[] before)
    {
        var targets = new List<NamedFunctionRow>();
        var placed = new List<string>();
        var missing = new List<string>();
        for (int i = 0; i < tags.Length; i++)
        {
            NamedFunctionRow[] exact = before.Where(row => row.Name == tags[i]).ToArray();
            if (exact.Length == 0)
            {
                missing.Add($"/unplaced/{i} No function has the exact tag '{tags[i]}'.");
            }
            foreach (NamedFunctionRow row in exact.Where(row => row.Placed))
            {
                string location = row.Page is null ? "has no verified unplaced status" : $"is placed on page {row.Page}";
                placed.Add($"/unplaced/{i} '{row.Name}' {location}. Delete placed functions with items from eplan_page_get include_drawings.");
            }
            targets.AddRange(exact);
        }
        if (placed.Count > 0)
        {
            placed.AddRange(missing);
            throw WriteArguments.Invalid(placed);
        }
        if (missing.Count > 0)
        {
            throw new BridgeErrorException(ErrorReason.DeviceNotFound, string.Join(" ", missing));
        }
        return targets.ToArray();
    }

    private static PlannedChange Change(NamedFunctionRow row, NamedFunctionRow[] before)
    {
        int count = before.Count(other => other.Name.StartsWith(row.Name + ":", StringComparison.Ordinal));
        return new PlannedChange
        {
            Action = "delete",
            Kind = "function",
            Name = $"{row.Name} ({row.Category}, unplaced)",
            OldValue = count > 0 ? $"{count} functions named {row.Name}:… exist" : null
        };
    }

    private static void VerifyCommitted(NamedFunctionRow[] before, NamedFunctionRow[] targets, string[] tags, NamedFunctionRow[] after)
    {
        var names = new HashSet<string>(tags, StringComparer.Ordinal);
        var deleted = new HashSet<(int, long)>(targets.Select(row => (row.Type, row.Oid)));
        var differences = after.Where(row => deleted.Contains((row.Type, row.Oid)) || names.Contains(row.Name) && !row.Placed)
            .Select(row => "remaining " + JsonConvert.SerializeObject(row));
        var expected = before.Where(row => !names.Contains(row.Name)).ToDictionary(row => (row.Type, row.Oid));
        var actual = after.Where(row => !names.Contains(row.Name)).ToDictionary(row => (row.Type, row.Oid));
        string[] details = differences.Concat(DependentDifferences(expected, actual)).Take(5).ToArray();
        if (details.Length > 0)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "Deletion left targets behind or changed other functions: " + string.Join("; ", details));
        }
    }

    private static IEnumerable<string> DependentDifferences(Dictionary<(int, long), NamedFunctionRow> expected,
        Dictionary<(int, long), NamedFunctionRow> actual)
    {
        foreach (var pair in expected)
        {
            string before = JsonConvert.SerializeObject(pair.Value);
            if (!actual.TryGetValue(pair.Key, out NamedFunctionRow? row))
            {
                yield return "missing " + before;
                continue;
            }
            string after = JsonConvert.SerializeObject(row);
            if (before != after)
            {
                yield return "changed " + before + " to " + after;
            }
        }
        foreach (var pair in actual.Where(pair => !expected.ContainsKey(pair.Key)))
        {
            yield return "added " + JsonConvert.SerializeObject(pair.Value);
        }
    }
}

public sealed class UnplacedFunctionDeleteExecuteOperation(IProjectCatalog catalog, IUnplacedFunctionRemover remover,
    ITransactionScopeFactory transactions, Action<OpenProject, BackupTarget?> backup)
    : UnplacedFunctionDeleteOperation(catalog, remover, transactions, true, backup), IWriteOperation;
