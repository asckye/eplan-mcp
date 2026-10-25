using System.Collections.Immutable;
using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

internal static class PlanVerificationTestData
{
    public static PlanBaseline Baseline() => PlanScopeTestData.Baseline();

    public static PlanStep Compile(string op, PlanBaseline? baseline = null)
    {
        baseline ??= Baseline();
        PlanScope whole = PlanScopeRules.Whole(baseline.Snapshot);
        SnapshotObject[] functions = [.. baseline.Snapshot.Pages.SelectMany(p => p.Objects).Where(o => o.Name == "+A1-K1")];
        PlanScope modify = op switch
        {
            "page.create" => PlanScopeRules.Pages(["+A1/3"]),
            "page.update" or "macro.insert" => PlanScopeRules.Pages(["+A1/1"]),
            "page.delete" or "device.update" => new(["+A1/1", "+A1/2"], [],
                [.. baseline.Snapshot.Pages.SelectMany(p => p.Objects).Where(o => functions.Contains(o) || op == "page.delete" && o.Name == "+A1/1")
                    .Select(o => PlanScopeRules.Locator(baseline.Snapshot.Project.Path, o))]),
            _ => whole,
        };
        var impact = new PlanImpact(whole, modify, [], baseline.NamesHash,
            op == "page.delete" ? [new("+A1-K1", [PlanScopeRules.Locator(baseline.Snapshot.Project.Path, functions.Single(o => o.Oid == 12))])] : []);
        return PlanStepSemantics.Build(baseline, PlanStepCompilationData.Input(op), impact, PlanStepCompilationData.Plan(op));
    }

    public static PlanSnapshot Copy(PlanSnapshot snapshot, Action<List<SnapshotPage>> edit, bool whole = true)
    {
        List<SnapshotPage> pages = JsonSerializer.Deserialize<List<SnapshotPage>>(JsonSerializer.Serialize(snapshot.Pages, PlanJson.Options), PlanJson.Options)!;
        edit(pages);
        foreach (SnapshotPage page in pages)
        {
            foreach (SnapshotObject obj in page.Objects)
            {
                obj.Hash = SnapshotCanonical.Sha256(SnapshotCanonical.Text(obj));
            }
            page.Hash = PlanSnapshotHashes.Page(page.Objects);
        }
        ImmutableDictionary<string, string> members = pages.SelectMany(p => p.Objects)
            .ToImmutableDictionary(o => PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), o => o.Hash, StringComparer.Ordinal);
        string hash = PlanSnapshotHashes.Compose(pages);
        return snapshot with
        {
            Pages = [.. pages],
            PageHashes = pages.ToImmutableDictionary(p => PlanSnapshotHashes.Key("page", p.Type, p.Oid), p => p.Hash, StringComparer.Ordinal),
            Members = members,
            MembersHash = PlanSnapshotHashes.Members(snapshot.Project.Path, members),
            ComposedHash = hash,
            SnapshotHash = whole ? hash : null,
        };
    }

    public static void Description(SnapshotObject obj, string? value, string language = "en_US")
    {
        SnapshotValue? property = obj.Values.SingleOrDefault(v => v.Id == 11011);
        if (property is null)
        {
            property = new() { Id = 11011, Name = "PAGE_NOMINATIOMN", Texts = [] };
            obj.Values.Add(property);
        }
        property.Texts!.RemoveAll(t => t.Language == language);
        if (value is not null)
        {
            property.Texts.Add(new() { Language = language, Text = value });
        }
    }

    public static PlanSnapshot After(string op, PlanSnapshot before) => Copy(before, pages =>
    {
        if (op == "page.create")
        {
            SnapshotPage created = PlanScopeTestData.Baseline([], ["+A1/3"]).Snapshot.Pages[0];
            created.Oid = 4;
            created.Objects[0].Oid = 4;
            Description(created.Objects[0], "Power");
            pages.Add(created);
        }
        else if (op == "page.update")
        {
            Description(pages[0].Objects[0], "New");
        }
        else if (op == "page.delete")
        {
            pages.RemoveAt(0);
        }
        else if (op == "page.renumber")
        {
            pages[0].Name = "+A1/10";
            pages[0].Objects[0].Name = "+A1/10";
            foreach (SnapshotObject obj in pages[0].Objects.Skip(1))
            {
                obj.Page = "+A1/10";
            }
        }
        else if (op is "device.update" or "device.renumber")
        {
            foreach (SnapshotObject obj in pages.SelectMany(p => p.Objects).Where(o => o.Name == "+A1-K1"))
            {
                obj.Name = op == "device.update" ? "+A1-K9" : "+A1-K10";
            }
        }
        else
        {
            pages[0].Objects.Add(PlanScopeTestData.Function(21, "+A1/1", "+A1-K8", true));
        }
    });

    public static PlanDefinition Definition(params PlanStep[] steps) => PlanTestData.Definition() with
    {
        Project = Baseline().Snapshot.Project,
        Steps = [.. steps],
    };

    public static PlanVerification Verify(PlanStep step, PlanSnapshot? after, string execution = ExecutionState.Committed,
        PlanSnapshot? before = null) => PlanVerifier.Verify(Definition(step), new(Baseline().Snapshot),
            [new(step.Index, execution, new(before ?? Baseline().Snapshot), after is null ? null : new(after))]);
}
