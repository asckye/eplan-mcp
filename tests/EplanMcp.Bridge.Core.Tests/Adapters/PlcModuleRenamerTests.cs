using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Eplan.Adapters;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class PlcModuleRenamerTests
{
    [Fact]
    public void TerminalOnlyModuleUsesARepresentativePointWithNameService()
    {
#pragma warning disable CA2000 // Borrowed native model fakes.
        var page = new Page();
        var point = new Function { Name = "=GAA+A1-A?:1.1", FunctionCategory = FunctionCategory.PLCTerminal, Page = page };
        var second = new Function(2) { Name = "=GAA+A1-A?:1.2", FunctionCategory = FunctionCategory.PLCTerminal, Page = page };
        page.AllPlacements = [point, second];
        EplanProjects.Project = new Project(page)
        {
            ParseName = (category, name, parsed, _) =>
            {
                Assert.Equal((int)FunctionCategory.PLCTerminal, category);
                Assert.Equal("=GAA+A1-A1", name);
                parsed.FUNC_CODE = "A";
                parsed.FUNC_COUNTER = "1";
            }
        };
#pragma warning restore CA2000
        NameService.Rename = (representative, _, cdps, keep) =>
        {
            Assert.Same(point, representative);
            Assert.False(cdps);
            Assert.True(keep);
            point.Name = "=GAA+A1-A1:1.1";
            second.Name = "=GAA+A1-A1:1.2";
        };
        try
        {
            var adapter = new EplanDeviceRenamer();
            Assert.Equal(2, adapter.ReadDevice("project", "=GAA+A1-A?")!.Functions.Count);
            adapter.Rename("project", "=GAA+A1-A?", "=GAA+A1-A1");
            Assert.Null(adapter.ReadDevice("project", "=GAA+A1-A?"));
            Assert.Equal(2, adapter.ReadDevice("project", "=GAA+A1-A1")!.Functions.Count);
        }
        finally { NameService.Rename = null; }
    }
}
