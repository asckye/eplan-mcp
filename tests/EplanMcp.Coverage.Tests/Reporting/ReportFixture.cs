using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Scanning;
using EplanMcp.Coverage.Tests.Mapping;

namespace EplanMcp.Coverage.Tests.Reporting;

/// <summary>
/// The fake repository of the mapping tests with a tiny catalog in the format of the catalog extractor and the action
/// catalog (catalog/eplan-2026.0.3), a scan that touches every kind of unit, and an overlay.
/// </summary>
internal static class ReportFixture
{
    public const string Page = "Eplan.EplApi.Test.Page";

    public static TempRepository Create(bool withCatalog = true)
    {
        TempRepository repository = FakeRepository.Create();
        if (withCatalog)
        {
            WriteCatalog(repository);
        }

        repository.Write(Overlay.RelativePath, """
            live:
              eplan_page_list:
                - { date: 2026-10-19, eplan: 2026.0.3.25421, variant: Electric P8, result: pass, basis: stated, source: api-notes A12 }
            api:
              - { match: "N:Eplan.EplApi.Test.Planning", status: env_blocked, condition: 授权中没有, source: api-notes A24 }
            availability:
              - { scope: EPLAN 2026.0.3.25421, status: verified, note: 全部记录, source: api-notes A12 }
            """);
        return repository.Write(BridgeUsage.RelativePath, CoverageJson.Serialize(new BridgeUsage
        {
            EplanApi = "2026.0.3.25421",
            ScannedOn = "2026-10-21",
            ScannedSources = "a73de2f",
            SourceHash = BridgeUsage.ComputeSourceHash(repository.Root),
            Operations = [new("page.create.execute", "C", null), new("page.create.plan", "P", null), new("page.list", "L", null)],
            Usages = new(StringComparer.Ordinal)
            {
                ["A:EXPORT/pdf"] = [Site("page.create.plan")],
                ["F:Eplan.EplApi.Test.Kind.A"] = [Site()],
                [$"M:{Page}.#ctor"] = [Site("page.list")],
                [$"M:{Page}.Create(System.String)"] = [Site("page.create.execute")],
                [$"M:{Page}.Missing"] = [Site("page.list")],
                [$"M:{Page}.Remove"] = [Site()],
                [$"P:{Page}.Name"] = [Site("page.list")],
                ["P:Eplan.EplApi.Test.PagePropertyList.PAGE_NAME(System.Int32)"] = [Site("page.list")],
            },
        }));
    }

    private static void WriteCatalog(TempRepository repository)
    {
        const string Directory = "catalog/eplan-2026.0.3/";
        string type = $"\"type\":\"T:{Page}\"";
        repository
            .Write(Directory + "types.jsonl", $$$"""
                {"id":"T:{{{Page}}}","kind":"class","layer":"type","ns":"Eplan.EplApi.Test"}
                {"id":"T:Eplan.EplApi.Test.Kind","kind":"enum","layer":"type","ns":"Eplan.EplApi.Test","values":{"A":"0","B":"1"}}

                """)
            .Write(Directory + "members/Eplan.EplApi.Test.jsonl", $$$"""
                {"id":"M:{{{Page}}}.#ctor","kind":"constructor","layer":"type",{{{type}}}}
                {"id":"M:{{{Page}}}.Create(System.String)","kind":"method","layer":"operation",{{{type}}},"group":"O:{{{Page}}}.Create"}
                {"id":"M:{{{Page}}}.Create(System.String,System.Int32)","kind":"method","layer":"operation",{{{type}}},"group":"O:{{{Page}}}.Create"}
                {"id":"P:{{{Page}}}.Name","kind":"property","layer":"operation",{{{type}}},"group":"O:{{{Page}}}.Name"}
                {"id":"M:{{{Page}}}.Remove","kind":"method","layer":"operation",{{{type}}},"group":"O:{{{Page}}}.Remove","documented":false}
                {"id":"M:{{{Page}}}.Old","kind":"method","layer":"operation",{{{type}}},"group":"O:{{{Page}}}.Old","obsolete":true,"excluded":{"reason":"deprecated","rule":"obsolete_attribute"}}

                """)
            .Write(Directory + "members/Eplan.EplApi.Test.Planning.jsonl", """
                {"id":"M:Eplan.EplApi.Test.Planning.Plan.Run","kind":"method","layer":"operation","type":"T:Eplan.EplApi.Test.Planning.Plan","group":"O:Eplan.EplApi.Test.Planning.Plan.Run"}

                """)
            .Write(Directory + "data-dictionary.jsonl", """
                {"object":"Page","name":"PAGE_NAME","value":"11000"}
                {"object":"Page","name":"PAGE_OLD","value":"11001","obsolete":true}
                {"object":"Retired","name":"RETIRED_ONE","value":"12000","obsolete":true}

                """)
            .Write(Directory + "actions.json", """
                [
                  { "id": "A:export/DXF", "action": "export", "type": "DXF", "status": "registered" },
                  { "id": "A:export/PDF", "action": "export", "type": "PDF", "status": "registered" },
                  { "id": "A:print", "action": "print", "type": null, "status": "excluded", "reason": "printing" }
                ]
                """);
    }

    private static UsageSite Site(params string[] operations) => new("Adapter", "Member", "src/EplanMcp.Bridge.Eplan/Adapters/A.cs", ["call"], operations);
}
