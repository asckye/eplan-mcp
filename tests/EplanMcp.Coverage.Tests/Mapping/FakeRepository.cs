using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage.Tests.Mapping;

/// <summary>
/// A repository with two tools: eplan_page_list (read, by constant) and eplan_page_create (write, by literal, whose
/// operations are in a helper), their specs, tests, the contract snapshot and a scan result.
/// </summary>
internal static class FakeRepository
{
    public static TempRepository Create()
    {
        var repository = new TempRepository()
            .Write("src/EplanMcp.Bridge.Eplan/Adapters/A.cs", "class A { }")
            .Write("src/EplanMcp.Bridge.Core/B.cs", "class B { }")
            .Write("src/EplanMcp.Contracts/OperationNames.cs", """
                public static class OperationNames
                {
                    public const string PageList = "page.list";
                    public const string PageCreatePlan = "page.create.plan";
                    public const string PageCreateExecute = "page.create.execute";
                }
                """)
            .Write("src/EplanMcp.Host/Tools/PageTools.cs", """
                internal sealed class PageTools
                {
                    public const string ListTool = "eplan_page_list";

                    [McpServerTool(Name = ListTool, ReadOnly = true)]
                    public static Task ListAsync(IBridgeClient bridge) => bridge.InvokeAsync(OperationNames.PageList);

                    [McpServerTool(Name = "eplan_page_create", ReadOnly = false)]
                    public static Task CreateAsync(IBridgeClient bridge) => Run(bridge);

                    private static Task Run(IBridgeClient bridge) => bridge.WriteAsync(OperationNames.PageCreatePlan, OperationNames.PageCreateExecute);
                }
                """)
            .Write("tests/Host.Tests/PageToolTests.cs", """
                [Trait("Size", "Small")]
                public sealed class PageListToolTests { void T() => Assert.Equal(PageTools.ListTool, ""); }
                [Trait("Size", "Large")]
                public sealed class PageCreateLiveTests { void T() => Call("eplan_page_create"); }
                public sealed class NotATestClass { void T() => Call("eplan_page_list"); }
                [Trait("Size", "Small")]
                public sealed class PageListOperationTests { void T() => new PageListOperation(null); }
                """)
            .Write("docs/eplan/api-notes.md", "# notes\n### A11. one\n### A12. two\n")
            .Write("docs/tools/eplan_page_list.md", "# eplan_page_list\n| 状态 | Proposed（M2） |\n\nVerified on `+A1` and `A11` (A12、A99). Device +A1-K1.\n")
            .Write("tests/Contracts.Snapshots/server-contract.verified.json", """
                { "tools": [
                  { "name": "eplan_page_create", "annotations": { "readOnlyHint": false, "destructiveHint": false } },
                  { "name": "eplan_page_list", "annotations": { "readOnlyHint": true } }
                ] }
                """);
        return repository.Write(BridgeUsage.RelativePath, CoverageJson.Serialize(new BridgeUsage
        {
            EplanApi = "2026.0.3.25421",
            ScannedOn = "2026-10-21",
            ScannedSources = "a73de2f",
            SourceHash = BridgeUsage.ComputeSourceHash(repository.Root),
            Operations =
            [
                new OperationEntry("page.create.execute", "PageCreateExecuteOperation", null),
                new OperationEntry("page.create.plan", "PageCreatePlanOperation", null),
                new OperationEntry("page.list", "PageListOperation", null),
            ],
            Usages = new(StringComparer.Ordinal)
            {
                ["A:act"] = [Site("page.create.plan")],
                ["M:X.A"] = [Site("page.list")],
                ["M:X.B"] = [Site("page.create.execute"), Site()],
                ["T:X"] = [Site()],
            },
        }));
    }

    private static UsageSite Site(params string[] operations) => new("Adapter", "Member", "src/EplanMcp.Bridge.Eplan/Adapters/A.cs", ["call"], operations);
}
