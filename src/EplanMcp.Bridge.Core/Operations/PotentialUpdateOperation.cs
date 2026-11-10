using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads and writes the properties of potential definitions (and potential points, A129) on a page.</summary>
public interface IPotentialEditor
{
    /// <summary>The current values of <paramref name="names"/> for each locator, in order; PLAN_STALE when one is gone.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string?>> Read(string project, string page, IReadOnlyList<DrawingLocator> items, IReadOnlyList<string> names);

    /// <summary>Checks the targets now and returns the write, run inside the transaction.</summary>
    public Action Prepare(string project, string page, IReadOnlyList<(DrawingLocator Locator, IReadOnlyDictionary<string, string> Values)> changes);
}

/// <summary>
/// eplan_potential_update (docs/tools/eplan_potential_update.md): a read-only plan of the property changes, then one checked
/// transaction. Values are compared as text; potential_type is an integer and travels as its invariant text.
/// </summary>
public class PotentialUpdateOperation(IProjectCatalog catalog, IPageDrawingReader drawings, IPotentialEditor editor,
    ITransactionScopeFactory transactions, bool execute) : Operation<JObject, JObject>
{
    public const string Tool = "eplan_potential_update";

    /// <summary>The editable properties, as the tool names them.</summary>
    public static readonly string[] Names = ["potential_name", "signal_name", "color", "cross_section", "potential_value", "potential_type"];

    public override string Name => execute ? OperationNames.PotentialUpdateExecute : OperationNames.PotentialUpdatePlan;

    protected override JObject Execute(JObject args)
    {
        JObject input = execute ? args["request"] as JObject ?? throw WriteArguments.NotStarted(WriteArguments.Invalid(["Supply request."])) : args;
        OpenProject project;
        string page;
        List<(DrawingLocator Locator, Dictionary<string, string> Wanted)> items;
        IReadOnlyList<IReadOnlyDictionary<string, string?>> before;
        string fingerprint;
        Action write;
        try
        {
            (page, items) = Check(input);
            project = WriteArguments.Writable(catalog, (string)input["project"]!);
            PageDrawingGeometry geometry = drawings.Read(project.Path, page) ?? throw new BridgeErrorException(execute ? ErrorReason.PlanStale : ErrorReason.PageNotFound, "Read the target page with eplan_page_get.");
            foreach (var item in items)
            {
                DrawingPlacement row = geometry.Placements.FirstOrDefault(p => DrawingLocators.Matches(item.Locator, p.Locator)) ?? throw Stale();
                if (row.Kind != "PotentialDefinition")
                { throw WriteArguments.Invalid([$"Item {item.Locator.Type}/{item.Locator.Oid} is a {row.Kind}; only potential definitions and potential points (PotentialDefinition) are updated here."]); }
            }
            before = editor.Read(project.Path, page, [.. items.Select(i => i.Locator)], Names);
            fingerprint = Fingerprint.Of(items.Select((item, i) => item.Locator.Type + "/" + item.Locator.Oid + "\t" + string.Join("\t", Names.Select(n => n + "=" + before[i][n]))));
            if (execute && (string?)args["fingerprint"] != fingerprint)
            { throw Stale(); }
            write = editor.Prepare(project.Path, page, [.. Changes(items, before)]);
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var changes = Changes(items, before).ToList();
        var result = new JObject
        {
            ["project"] = project.Name,
            ["page"] = page,
            ["items"] = new JArray(items.Select((item, i) => new JObject
            {
                ["locator"] = JObject.FromObject(new { project = item.Locator.Project, page = item.Locator.Page, type = item.Locator.Type, oid = item.Locator.Oid, digest = item.Locator.Digest }),
                // Every current value, so the potential is recognisable by its name; after lists only the requested ones.
                ["before"] = new JObject(Names.Select(n => new JProperty(n, before[i][n]))),
                ["after"] = new JObject(item.Wanted.Select(w => new JProperty(w.Key, w.Value))),
            })),
            ["fingerprint"] = fingerprint,
        };
        if (!execute)
        {
            return (JObject)Protocol.BridgeJson.FromObject(new WritePlan
            {
                Project = project.Name,
                ProjectPath = project.Path,
                Fingerprint = fingerprint,
                Changes = [.. changes.SelectMany(c => c.Values.Select(v => new PlannedChange
                {
                    Action = "update",
                    Kind = "potential",
                    Name = (before[items.FindIndex(i => i.Locator == c.Locator)]["potential_name"] ?? "?") + " (" + c.Locator.Type + "/" + c.Locator.Oid + ") " + v.Key,
                    OldValue = before[items.FindIndex(i => i.Locator == c.Locator)][v.Key],
                    NewValue = v.Value,
                }))],
                Details = new() { ["preview"] = result.ToString(Formatting.None) },
            });
        }

        DrawingLocator[] locators = [.. items.Select(i => i.Locator)];
        Committed<bool> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            write();
            return true;
        }, verifyCommitted: _ =>
        {
            IReadOnlyList<IReadOnlyDictionary<string, string?>> after = editor.Read(project.Path, page, locators, Names);
            for (int i = 0; i < items.Count; i++)
            {
                foreach (var wanted in items[i].Wanted.Where(w => !Same(w.Key, after[i][w.Key], w.Value)))
                { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, $"EPLAN read back {wanted.Key} of {locators[i].Type}/{locators[i].Oid} as '{after[i][wanted.Key]}', not '{wanted.Value}'; nothing was changed."); }
            }
        }, verifyRollback: () =>
        {
            IReadOnlyList<IReadOnlyDictionary<string, string?>> restored = editor.Read(project.Path, page, locators, Names);
            if (restored.Where((values, i) => Names.Any(n => values[n] != before[i][n])).Any())
            { throw new InvalidOperationException("Potential update rollback did not restore the values."); }
        });
        result["execution"] = ExecutionState.Committed;
        result["warning"] = committed.Warning;
        result["hint"] = changes.Count == 0 ? "Every value was already as requested; nothing was written."
            : "Call eplan_connection_generate with rebuild:true for the pages whose wires carry these potentials, so they take the new colour and cross-section.";
        return result;
    }

    /// <summary>The requested values that differ from the current ones, per item; items without a difference are left out.</summary>
    private static IEnumerable<(DrawingLocator Locator, IReadOnlyDictionary<string, string> Values)> Changes(
        List<(DrawingLocator Locator, Dictionary<string, string> Wanted)> items, IReadOnlyList<IReadOnlyDictionary<string, string?>> before) =>
        items.Select((item, i) => (item.Locator, (IReadOnlyDictionary<string, string>)item.Wanted.Where(w => !Same(w.Key, before[i][w.Key], w.Value)).ToDictionary(w => w.Key, w => w.Value)))
            .Where(c => c.Item2.Count > 0);

    // EPLAN stores cross-sections with a decimal comma (eplan_connection_create writes them so); 1.5 and 1,5 are the same value.
    private static bool Same(string name, string? actual, string wanted) =>
        string.Equals(actual ?? string.Empty, wanted, StringComparison.Ordinal)
        || name == "cross_section" && string.Equals((actual ?? string.Empty).Replace(',', '.'), wanted.Replace(',', '.'), StringComparison.Ordinal);

    private static (string Page, List<(DrawingLocator, Dictionary<string, string>)> Items) Check(JObject input)
    {
        var violations = new List<string>();
        if (input.Properties().Any(p => p.Name is not ("project" or "page" or "items" or "dry_run")))
        { violations.Add("Only project, page, items and dry_run are accepted."); }
        if (input["project"] is not JValue { Type: JTokenType.String } project || string.IsNullOrWhiteSpace((string?)project))
        { violations.Add("'project' is required."); }
        if (input["page"] is not JValue { Type: JTokenType.String } page || string.IsNullOrWhiteSpace((string?)page))
        { violations.Add("'page' is required: the page that holds the potential definitions."); }
        if (input["dry_run"] is { Type: not JTokenType.Boolean })
        { violations.Add("'dry_run' must be boolean."); }
        var items = new List<(DrawingLocator, Dictionary<string, string>)>();
        if (input["items"] is not JArray { Count: >= 1 and <= 50 } list)
        { violations.Add("'items' needs 1-50 entries {locator, potential_name?, signal_name?, color?, cross_section?, potential_value?, potential_type?}."); }
        else
        {
            for (int i = 0; i < list.Count; i++)
            {
                string at = "/items/" + i.ToString(CultureInfo.InvariantCulture);
                if (list[i] is not JObject item || item.Properties().Any(p => p.Name != "locator" && !Names.Contains(p.Name)))
                { violations.Add(at + ": use locator and the property names " + string.Join(", ", Names) + "."); continue; }
                if (item["locator"] is not JObject locator || locator.Count != 5 || locator["type"]?.Type != JTokenType.Integer || locator["oid"]?.Type != JTokenType.Integer
                    || locator["digest"] is not JValue { Type: JTokenType.String } digest || ((string)digest!).Length != 64 || locator["project"]?.Type != JTokenType.String || locator["page"]?.Type != JTokenType.String)
                { violations.Add(at + "/locator: use the PotentialDefinition locator from eplan_page_get (include_drawings) or eplan_object_find."); continue; }
                var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string name in Names.Where(n => item[n] is not null))
                {
                    JToken value = item[name]!;
                    if (name == "potential_type" ? value.Type != JTokenType.Integer
                        : value.Type != JTokenType.String || ((string)value!).Trim().Length == 0 || ((string)value!).Length > 200 || ((string)value!).Any(char.IsControl))
                    { violations.Add($"{at}/{name}: " + (name == "potential_type" ? "use an integer." : "use 1-200 characters without control characters.")); continue; }
                    wanted[name] = name == "potential_type" ? ((long)value).ToString(CultureInfo.InvariantCulture) : (string)value!;
                }
                if (wanted.Count == 0)
                { violations.Add(at + ": give at least one property to change."); }
                items.Add((locator.ToObject<DrawingLocator>()!, wanted));
            }
            if (items.Select(i => i.Item1.Type + "/" + i.Item1.Oid).Distinct(StringComparer.Ordinal).Count() != items.Count)
            { violations.Add("'items' lists a potential definition twice."); }
        }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        return ((string)input["page"]!, items);
    }

    private static BridgeErrorException Stale() => new(ErrorReason.PlanStale, "A potential definition or its values changed; read the page with eplan_page_get and call eplan_potential_update again.");
}

public sealed class PotentialUpdateExecuteOperation(IProjectCatalog catalog, IPageDrawingReader drawings, IPotentialEditor editor, ITransactionScopeFactory transactions)
    : PotentialUpdateOperation(catalog, drawings, editor, transactions, true), IWriteOperation;
