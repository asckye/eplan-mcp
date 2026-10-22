using EplanMcp.Coverage.Mapping;

namespace EplanMcp.Coverage.Tests.Mapping;

[Trait("Size", "Medium")]
public sealed class ToolMappingTests : IDisposable
{
    private readonly TempRepository _repository = FakeRepository.Create();

    [Fact]
    public void Build_AReadTool_MapsItsOperationToTheApiItReaches_WithItsEvidence()
    {
        ToolEntry list = ToolMapping.Build(_repository.Root).Tools.Single(t => t.Name == "eplan_page_list");

        Assert.Equal(("read", "src/EplanMcp.Host/Tools/PageTools.cs"), (list.Safety, list.Host));
        Assert.Equal(["page.list"], list.Operations);
        Assert.Equal(["M:X.A"], list.Api);
        Assert.Equal(("docs/tools/eplan_page_list.md", "Proposed（M2）"), (list.Evidence.Spec, list.Evidence.SpecStatus));
        Assert.Equal(["A12"], list.Evidence.ApiNotes); // not A11 in code, not the unknown A99, not the tag +A1
        Assert.Equal(["PageListToolTests"], list.Evidence.Tests.Select(t => t.Name));
        Assert.Equal(("PageListOperationTests", "Small"), (Assert.Single(list.Evidence.OperationTests).Name, list.Evidence.OperationTests[0].Size));
    }

    [Fact]
    public void Build_AWriteToolWhoseOperationsAreInAHelper_GetsBothOperationsTheirApiAndActions()
    {
        ToolMapping mapping = ToolMapping.Build(_repository.Root);
        ToolEntry create = mapping.Tools[0];

        Assert.Equal(("eplan_page_create", "write"), (create.Name, create.Safety));
        Assert.Equal(["page.create.execute", "page.create.plan"], create.Operations);
        Assert.Equal(["M:X.B"], create.Api);
        Assert.Equal(["A:act"], create.Actions);
        Assert.Equal(("PageCreateLiveTests", "Large"), (Assert.Single(create.Evidence.Tests).Name, create.Evidence.Tests[0].Size));
        Assert.Null(create.Evidence.Spec);
        Assert.Equal(["T:X"], mapping.Infrastructure);
    }

    [Fact]
    public void Build_AToolOfTheSnapshotWithoutHostMethod_Fails()
    {
        _repository.Write("tests/Contracts.Snapshots/server-contract.verified.json", """{ "tools": [ { "name": "eplan_gone", "annotations": {} } ] }""");

        Assert.Contains("eplan_gone", Assert.Throws<InvalidDataException>(() => ToolMapping.Build(_repository.Root)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_AfterGenerate_IsClean_ButAChangedSpecMakesToolsJsonStale()
    {
        CoverageCheck.Generate(_repository.Root);
        List<string> generated = CoverageCheck.Problems(_repository.Root);
        _repository.Write("docs/tools/eplan_page_list.md", "| 状态 | Implemented |\n");
        List<string> afterSpecChange = CoverageCheck.Problems(_repository.Root);

        Assert.Empty(generated);
        Assert.StartsWith(ToolMapping.RelativePath, Assert.Single(afterSpecChange), StringComparison.Ordinal);
    }

    public void Dispose() => _repository.Dispose();
}
