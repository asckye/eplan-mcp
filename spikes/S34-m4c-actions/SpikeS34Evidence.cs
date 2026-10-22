using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S34;

public sealed partial class SpikeS34Action
{
    private static JObject CableSchemes(Project project)
    {
        var result = new JObject
        {
            ["api"] = "ProjectSchemeSetting.Init/GetCount/SetScheme/GetName",
            ["nodeSource"] = "S21 README: project Settings.xdb read-only inspection; api-notes B9/B10. No guessed node paths.",
        };
        foreach (string node in new[] { "CableLog.CableCreateScheme", "CableLog.CableNumberingScheme", "CableLog.CableAutoSelectionScheme" })
        {
            Capture(result, node, () =>
            {
                using var scheme = new ProjectSchemeSetting();
                scheme.Init(node, project);
                int count = scheme.GetCount();
                var names = new List<JObject>();
                var errors = new JArray();
                for (int i = 0; i < Math.Min(count, 200); i++)
                {
                    try
                    {
                        // B9: selects the reader's scheme; does not call SetLastUsed or modify the scheme.
                        scheme.SetScheme(i);
                        string name = scheme.GetName();
                        if (!string.Equals(name, "InternalRefDefScheme", StringComparison.Ordinal))
                        {
                            names.Add(new JObject { ["name"] = name });
                        }
                    }
                    catch (Exception ex)
                    {
                        if (errors.Count < 8)
                        {
                            errors.Add(new JObject { ["index"] = i, ["error"] = Describe(ex) });
                        }
                    }
                }

                JObject listed = Listed(names, 200);
                listed["nodeSchemeCount"] = count;
                listed["unscanned"] = Math.Max(0, count - 200);
                listed["errors"] = errors;
                Capture(listed, "lastUsed", () => new JValue(scheme.GetLastUsed()));
                return listed;
            });
        }

        return result;
    }

    private static NameSnapshot Names(Project project, bool cables)
    {
        var snapshot = new NameSnapshot();
        using var finder = new DMObjectsFinder(project);
        using var filter = new FunctionsFilter { FunctionCategory = FunctionCategory.Cable };
        Function[] functions = finder.GetFunctions(cables ? filter : null);
        // Finder results are EPLAN-owned StorableObjects; disposing them corrupts EPLAN's native heap (S34 r2).
        foreach (Function function in functions)
        {
            try
            {
                long id = function.ObjectIdentifier;
                var row = new JObject { ["id"] = id, ["isPlcBox"] = false };
                snapshot.Items.Add(id, row);
                Capture(row, "fullName", () => new JValue(function.Name));
                Capture(row, "visibleName", () => new JValue(function.VisibleName));
                Capture(row, "category", () => new JValue(function.FunctionCategory.ToString()));
                if (cables)
                {
                    Capture(row, "automatic", () =>
                    {
                        // data-dictionary.jsonl: Cable.FUNC_CABLEAUTOCREATED = 20059;
                        // installed API XML: FunctionPropertyList.FUNC_CABLEAUTOCREATED is Boolean.
                        // Acquire Properties once for this object's read; never reacquire it after disposal (A36).
                        using var properties = function.Properties;
                        using PropertyValue value = properties.FUNC_CABLEAUTOCREATED;
                        return value.IsEmpty ? JValue.CreateNull() : new JValue(value.ToBool());
                    });
                }
            }
            catch (Exception ex)
            {
                snapshot.RecordError(ex);
            }
        }

        if (!cables)
        {
            foreach (var plc in finder.GetPLCs(null))
            {
                try
                {
                    if (snapshot.Items.TryGetValue(plc.ObjectIdentifier, out JObject? row))
                    {
                        row["isPlcBox"] = true;
                    }
                    else
                    {
                        var added = new JObject { ["id"] = plc.ObjectIdentifier, ["isPlcBox"] = true };
                        snapshot.Items.Add(plc.ObjectIdentifier, added);
                        Capture(added, "fullName", () => new JValue(plc.Name));
                        Capture(added, "category", () => new JValue(plc.FunctionCategory.ToString()));
                    }
                }
                catch (Exception ex)
                {
                    snapshot.RecordError(ex);
                }
            }
        }

        return snapshot;
    }

    private static void CableStage(JObject result, string stage, Project project, NameSnapshot? before)
    {
        NameSnapshot? after = null;
        Capture(result, "cables" + stage, () => { after = Names(project, true); return after.Report(200); });
        Capture(result, "cableChanges" + stage, () => CableChanges(before, after));
    }

    private static JObject CableChanges(NameSnapshot? before, NameSnapshot? after)
    {
        if (before is null || after is null)
        {
            return new JObject { ["unavailable"] = "One cable snapshot failed; missing objects are not inferred." };
        }

        var renamed = new List<JObject>();
        var flags = new List<JObject>();
        foreach (KeyValuePair<long, JObject> pair in before.Items)
        {
            if (after.Items.TryGetValue(pair.Key, out JObject? current))
            {
                JObject old = pair.Value;
                if ((Name(old) is not null && Name(current) is not null && !JToken.DeepEquals(old["fullName"], current["fullName"])) ||
                    (old["visibleName"]?.Type == JTokenType.String && current["visibleName"]?.Type == JTokenType.String && !JToken.DeepEquals(old["visibleName"], current["visibleName"])))
                {
                    renamed.Add(new JObject { ["id"] = pair.Key, ["before"] = old.DeepClone(), ["after"] = current.DeepClone() });
                }

                if (old["automatic"]?.Type == JTokenType.Boolean && current["automatic"]?.Type == JTokenType.Boolean && !JToken.DeepEquals(old["automatic"], current["automatic"]))
                {
                    flags.Add(new JObject { ["id"] = pair.Key, ["fullName"] = current["fullName"]?.DeepClone(), ["before"] = old["automatic"]!.DeepClone(), ["after"] = current["automatic"]!.DeepClone() });
                }
            }
        }

        return new JObject
        {
            ["comparisonComplete"] = before.Complete && after.Complete,
            ["renamed"] = Listed(renamed, 200), ["automaticChanged"] = Listed(flags, 200),
            ["appeared"] = Listed(after.Items.Where(p => !before.Items.ContainsKey(p.Key)).Select(p => p.Value), 200),
            ["disappeared"] = Listed(before.Items.Where(p => !after.Items.ContainsKey(p.Key)).Select(p => p.Value), 200),
        };
    }

    private static JObject PlcAdded(NameSnapshot? before, NameSnapshot after)
    {
        if (before is null)
        {
            return new JObject { ["unavailable"] = "The before snapshot failed; added objects are not inferred." };
        }

        var existingNames = new HashSet<string>(before.Items.Values.Select(Name).OfType<string>().Where(n => n.Length > 0), StringComparer.Ordinal);
        JObject[] added = after.Items.Where(p => !before.Items.ContainsKey(p.Key)).Select(p => p.Value).ToArray();
        var occurrences = added.Select(Name).OfType<string>().GroupBy(n => n, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (JObject row in added)
        {
            string? name = Name(row);
            row["duplicatesExistingFullName"] = name is null || name.Length == 0 ? JValue.CreateNull() : new JValue(existingNames.Contains(name));
            row["duplicatesAnotherAddedFullName"] = name is null || name.Length == 0 ? JValue.CreateNull() : new JValue(occurrences[name] > 1);
        }

        JObject result = Listed(added.OrderByDescending(r => r.Value<bool>("isPlcBox")).ThenBy(Name, StringComparer.Ordinal).ThenBy(r => r.Value<long>("id")), 50);
        result["comparisonComplete"] = before.Complete && after.Complete;
        result["before"] = before.Summary();
        result["after"] = after.Summary();
        result["addedPlcBoxes"] = added.Count(r => r.Value<bool>("isPlcBox"));
        result["duplicateExistingNameCount"] = added.Count(r => r.Value<bool?>("duplicatesExistingFullName") == true);
        result["duplicateWithinAddedNameCount"] = added.Count(r => r.Value<bool?>("duplicatesAnotherAddedFullName") == true);
        result["selection"] = "All newly identified functions from this PLC import; PLC boxes first, at most 50 rows. Identity comparison precedes truncation.";
        return result;
    }

    private static string? Name(JObject row) => row["fullName"]?.Type == JTokenType.String ? row.Value<string>("fullName") : null;

    private static JObject Listed(IEnumerable<JObject> rows, int limit)
    {
        JObject[] all = rows.ToArray();
        return new JObject { ["total"] = all.Length, ["entriesOmitted"] = Math.Max(0, all.Length - limit), ["entries"] = new JArray(all.Take(limit).Select(o => o.DeepClone())) };
    }

    private sealed class NameSnapshot
    {
        public Dictionary<long, JObject> Items { get; } = [];

        private JArray Errors { get; } = [];

        private int ErrorCount { get; set; }

        public bool Complete => ErrorCount == 0 && Items.Values.All(r => Name(r) is not null && !r.Properties().Any(p => p.Value is JObject));

        public void RecordError(Exception ex)
        {
            ErrorCount++;
            if (Errors.Count < 8)
            {
                Errors.Add(Describe(ex));
            }
        }

        public JObject Summary()
        {
            JObject[] fieldErrors = Items.Values.SelectMany(row => row.Properties().Where(p => p.Value is JObject)
                .Select(p => new JObject { ["id"] = row["id"]!.DeepClone(), ["field"] = p.Name, ["error"] = p.Value.DeepClone() })).ToArray();
            return new JObject { ["total"] = Items.Count, ["complete"] = Complete, ["objectErrors"] = ErrorCount, ["errors"] = Errors.DeepClone(), ["fieldErrors"] = Listed(fieldErrors, 8) };
        }

        public JObject Report(int limit)
        {
            JObject report = Listed(Items.Values.OrderBy(Name, StringComparer.Ordinal).ThenBy(r => r.Value<long>("id")), limit);
            foreach (JProperty property in Summary().Properties())
            {
                report[property.Name] = property.Value.DeepClone();
            }

            return report;
        }
    }
}
