using EplanMcp.Coverage.Mapping;

namespace EplanMcp.Coverage.Tests.Mapping;

[Trait("Size", "Medium")]
public sealed class OverlayTests : IDisposable
{
    private readonly TempRepository _repository = FakeRepository.Create();

    [Fact]
    public void Build_LiveRecordsAndNotes_ReachTheirTool_AndOnlyStatedPassesVerify()
    {
        _repository.Write(Overlay.RelativePath, """
            live:
              eplan_page_list:
                - { date: 2026-10-19, eplan: 2026.0.3.25421, variant: Electric P8, result: pass, basis: stated, source: api-notes A12 }
                - { date: 2026-10-20, result: pass, basis: inferred, source: api-notes A20, note: 未写工具名 }
                - { date: 2026-10-21, result: partial, basis: stated, source: docs/tools/eplan_page_list.md }
            tools:
              eplan_page_create: { note: 只在测试中, operations: [page.list] }
            api:
              - { match: "N:Eplan.EplApi.Scripting", status: excluded, reason: unsafe, note: Code 级, source: ADR-0005 }
            """);

        ToolMapping mapping = ToolMapping.Build(_repository.Root);
        ToolEntry list = mapping.Tools.Single(t => t.Name == "eplan_page_list");

        Assert.Equal([true, false, false], list.Live.Select(r => r.Verifies));
        Assert.Equal(("2026.0.3.25421", "Electric P8"), (list.Live[0].Eplan, list.Live[0].Variant));
        ToolEntry create = mapping.Tools.Single(t => t.Name == "eplan_page_create");
        Assert.Equal("只在测试中", create.Note);
        Assert.Equal(["page.create.execute", "page.create.plan", "page.list"], create.Operations); // page.list from the overlay
        Assert.Equal(["M:X.A", "M:X.B"], create.Api);
    }

    [Theory]
    [InlineData("tools:\n  eplan_page_list: { operations: [page.gone] }\n", "page.gone")]
    [InlineData("live:\n  eplan_gone:\n    - { date: 2026-10-19, result: pass, basis: stated, source: x }\n", "eplan_gone is not a tool")]
    [InlineData("live:\n  eplan_page_list:\n    - { date: 19.10.2026, result: pass, basis: stated, source: x }\n", "a live record needs")]
    [InlineData("live:\n  eplan_page_list:\n    - { date: 2026-10-19, result: passed, basis: stated, source: x }\n", "a live record needs")]
    [InlineData("live:\n  eplan_page_list:\n    - { date: 2026-10-19, result: pass, basis: stated, sauce: x }\n", "sauce")]
    [InlineData("api:\n  - { match: \"N:Eplan.EplApi.Scripting\", status: excluded, note: n, source: ADR-0005 }\n", "excluded with a reason")]
    [InlineData("api:\n  - { match: \"N:Eplan.EplApi.Scripting\", status: excluded, reason: architecture, note: n, source: ADR-0005 }\n", "gui, internal")]
    [InlineData("api:\n  - { match: Eplan.EplApi, status: partial, note: n, source: s }\n", "an ID or N:<namespace>")]
    public void Build_AnOverlayWithAMistake_FailsNamingIt(string yaml, string expected)
    {
        _repository.Write(Overlay.RelativePath, yaml);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ToolMapping.Build(_repository.Root));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.StartsWith(Overlay.RelativePath, error.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _repository.Dispose();
}
