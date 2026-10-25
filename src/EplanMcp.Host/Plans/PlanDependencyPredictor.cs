using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Plans;

/// <summary>The two closed creation edges; new objects have business names and never fabricated locators.</summary>
internal sealed class PlanDependencyPredictor(IBridgeClient bridge)
{
    private static readonly string[] _unknown = ["graphics", "texts", "placeholders", "connections.withoutPage"];

    public async Task<PlanStep> PredictAsync(PlanBaseline baseline, PlanArgumentStep input, PlanStep creation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JsonElement args = input.Args;
        string[] pages = Targets(input);
        JsonElement[] created = [.. creation.Args.GetProperty("pages").EnumerateArray()];
        if (creation.Op != "page.create" || input.Op is not ("page.update" or "macro.insert") || !input.DependsOn.Contains(creation.Index)
            || pages.Length is < 1 or > 50 || pages.Distinct(StringComparer.OrdinalIgnoreCase).Count() != pages.Length
            || pages.Any(p => created.Count(c => c.GetProperty("name").GetString() == p) != 1))
        {
            throw Refused(input, "Read/modify overlap has no predictor; reference unique pages from one earlier page.create.");
        }

        var expected = new List<JsonElement>();
        var changes = new List<PlanChange>();
        var conditions = new List<PlanPrecondition>();
        PlanScope read = PlanScopeRules.Pages(pages);
        JsonElement? inspected = null;
        foreach (string page in pages)
        {
            JsonElement source = created.Single(p => p.GetProperty("name").GetString() == page);
            string type = source.GetProperty("page_type").GetString()!;
            string language = creation.Args.GetProperty("language").GetString()!;
            string? before = ToolArguments.Optional(source.GetProperty("description").GetString());
            if (input.Op == "page.update")
            {
                string updateLanguage = args.GetProperty("language").GetString()!;
                string? after = ToolArguments.Optional(args.GetProperty("pages").EnumerateArray()
                    .Single(p => p.GetProperty("name").GetString() == page).GetProperty("description").GetString());
                string? old = updateLanguage == language ? before : null;
                if (after?.Length > 500)
                {
                    throw Refused(input, "Page descriptions must contain at most 500 characters.");
                }
                expected.Add(Expected("value", page, new
                {
                    pageType = type,
                    language = updateLanguage,
                    description = after,
                    before = new
                    {
                        pageType = type,
                        language,
                        description = before,
                    },
                },
                    ["pageType", "values/11011/" + updateLanguage]));
                if (old != after)
                {
                    changes.Add(new("update", "page", page, "description (" + updateLanguage + ")", old, after, true));
                }
            }
            else
            {
                if (baseline.Names.Any(n => n.Kind == "page" && !baseline.Snapshot.Pages.Any(p => p.Name == n.Name)))
                {
                    throw new BridgeCallException(new BridgeError
                    {
                        Reason = ErrorReason.CapabilityMissing,
                        Message = "Macro prediction requires complete project page evidence.",
                        Metadata = new()
                        {
                            ["missing"] = "[\"snapshot:project_pages\"]",
                        },
                    });
                }
                double x = args.GetProperty("x").GetDouble();
                double y = args.GetProperty("y").GetDouble();
                if (type != "Circuit" || args.GetProperty("variant").GetInt32() != 0 || args.GetProperty("numbering").GetString() != "keep"
                    || !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0)
                {
                    throw Refused(input, "macro.insert prediction requires an empty Circuit page, a finite nonnegative position, MultiLine variant 0 and numbering keep.");
                }
                PlanMacroInspectData macro = await bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect,
                    new()
                    {
                        Macro = args.GetProperty("macro").GetString(),
                    }, cancellationToken).ConfigureAwait(false);
                if (macro.Sha256.Length != 64 || macro.Sha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                    || macro.ObjectCount < macro.Functions.Count || macro.Functions.Count > 10_000
                    || baseline.Snapshot.Pages.Sum(p => p.Objects.Count) + 1 > 10_000)
                {
                    throw Refused(input, "Macro inspection does not prove a bounded complete prediction.");
                }
                inspected = JsonSerializer.SerializeToElement(macro, PlanJson.Options);
                var functions = macro.Functions.Select(f => new
                {
                    f.Kind,
                    f.Category,
                    f.Main,
                    namePattern = PlanNamePattern.For(page, f),
                    visibleNamePattern = PlanNamePattern.Visible(f),
                }).ToArray();
                foreach (PlanOccupiedName occupied in baseline.Names.Where(n => n.Kind == "function"))
                {
                    string part = DevicePart(occupied.Name);
                    if (functions.Any(f => PlanNamePattern.Matches(f.namePattern, occupied.Name))
                        || macro.Functions.Any(f => f.Category != "Wire" && part.Length > 0
                            && PlanNamePattern.Matches(@"\A" + PlanNamePattern.Label(DevicePart(f.Name)) + @"\z", part)))
                    {
                        throw Refused(input, "A macro function name is occupied in the project; choose a conflict-free macro.");
                    }
                }
                read = PlanScopeRules.Pages(baseline.Snapshot.Pages.Select(p => p.Name).Concat(pages));
                conditions.AddRange(PlanPreconditions.Build(baseline, PlanScopeRules.Whole(baseline.Snapshot)));
                conditions.Add(new PlanResourceHash(macro.Sha256));
                expected.Add(Expected("macro", page, new
                {
                    macro.Sha256,
                    pageType = type,
                    args,
                    representation = "MultiLine",
                    macro.ObjectCount,
                    functionCount = functions.Length,
                    kindCounts = functions.GroupBy(f => f.Kind, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
                    functions,
                }, ["page.exists", "macro.fileHash", "macro.functions", "macro.position", "macro.numbering"]));
                changes.Add(new("create", "macro", page, null, null, Describe(args, macro), true));
            }
        }
        return new(input.Index, input.Op, args.Clone(), input.Tier, read, PlanScopeRules.Pages(pages), [.. conditions],
            [.. expected], [.. changes], input.DependsOn, JsonSerializer.SerializeToElement(new
            {
                prediction = true,
                fromStep = creation.Index,
                baseline.Snapshot.Project,
                description = inspected,
            }, PlanJson.Options), changes.Count > 0)
        {
            Coverage = JsonSerializer.SerializeToElement(new
            {
                pages,
                baselinePages = read.Pages.Except(pages, StringComparer.Ordinal),
                unknown = _unknown,
                baseline.Snapshot.SkippedConnections,
            }, PlanJson.Options),
        };

        JsonElement Expected(string tag, string page, object value, string[] properties) => JsonSerializer.SerializeToElement(new
        {
            tag,
            target = new
            {
                kind = "page",
                name = page,
            },
            value,
            prediction = true,
            coveredProperties = properties,
            source = new
            {
                step = creation.Index,
                operation = "page.create",
                evidence = input.Op == "macro.insert" ? OperationNames.PlanMacroInspect : "args",
            },
        }, PlanJson.Options);
    }

    internal static string[] Targets(PlanArgumentStep input) => input.Op == "macro.insert" ? [input.Args.GetProperty("page").GetString()!]
        : input.Op is "page.create" or "page.update" or "page.delete" or "page.renumber" or "device.renumber"
            ? [.. input.Args.GetProperty("pages").EnumerateArray().Select(p => p.ValueKind == JsonValueKind.String ? p.GetString()! : p.GetProperty("name").GetString()!)] : [];

    internal static BridgeCallException Refused(PlanArgumentStep input, string message) => new(new BridgeError
    {
        Reason = ErrorReason.InvalidArgument,
        Message = message,
        Metadata = new()
        {
            ["violations"] = JsonSerializer.Serialize(new[]
            {
                new PlanViolation("/steps/" + (input.Index - 1).ToString(CultureInfo.InvariantCulture) + "/args", message),
            }, PlanJson.Options),
        },
    });

    private static string DevicePart(string name) => name[(name.LastIndexOf('-') + 1)..].Split(':')[0];

    internal static string Describe(JsonElement args, PlanMacroInspectData macro)
    {
        string file = args.GetProperty("macro").GetString()!.Replace('\\', '/').Split('/')[^1];
        int count = macro.Functions.Where(f => f.Category != "Wire" && f.Name.Length > 0).Select(f => f.Name).Distinct(StringComparer.Ordinal).Count();
        return string.Format(CultureInfo.InvariantCulture, "{0} at ({1:0.##}, {2:0.##}), numbering keep, {3} {4}",
            file, args.GetProperty("x").GetDouble(), args.GetProperty("y").GetDouble(), count, count == 1 ? "device" : "devices");
    }
}
