using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Native objects belong to the fake project, matching EPLAN's borrowed wrappers.

[Trait("Size", "Small")]
public sealed class DevicePlacerTests
{
    private static readonly int[] _suffixes = [1, 2, 3, 5, 6, 7];

    [Theory]
    [InlineData("SIE.6ES7131-4BD01-0AA0", "=GAA+A1-KR85", 6, 1)]
    [InlineData("SIE.6ES7131-4BD01-0AA0", "", 6, 0)]
    [InlineData("SIE.3SB3400-0B", "=GAA+A1-KR85", 1, 1)]
    [InlineData("SIE.3SB3400-0B", "", 1, 0)]
    public void Parts_RenameOnceOnlyWhenTagIsGiven(string part, string tag, int count, int expectedRenames)
    {
        var page = new Page { Name = "=GAA+A1&EFS1/85" };
        Function[] functions = [.. Enumerable.Range(0, count).Select(index => new Function(index + 1)
        {
            Name = "=+-K3:" + _suffixes[index],
            IsMainFunction = index == 0,
            FunctionCategory = FunctionCategory.PLCTerminal,
            Page = index == 0 ? page : null!,
        })];
        var project = new Project(page) { UnplacedFunctions = functions[1..] };
        EplanProjects.Project = project;
        int renames = 0;
        project.ParseName = (category, name, parsed, additional) =>
        {
            Assert.Equal((int)FunctionCategory.PLCTerminal, category);
            Assert.Equal(tag, name);
            parsed.DESIGNATION_PLANT = "GAA";
            parsed.DESIGNATION_LOCATION = "A1";
            parsed.FUNC_CODE = "KR";
            parsed.FUNC_COUNTER = "85";
        };
        DeviceService.Create = (number, variant, target, point) =>
        {
            Assert.Equal(part, number);
            Assert.Equal("1", variant);
            Assert.Same(page, target);
            Assert.Equal((80, 160), (point.X, point.Y));
            page.AllPlacements = [functions[0]];
            return functions;
        };
        foreach (Function function in functions)
        { function.Properties.FUNC_IDENTDEVICETAG = "=+-K3"; }
        NameService.Rename = (main, name, cdps, keep) =>
        {
            renames++;
            Assert.Same(functions[0], main);
            Assert.False(cdps);
            Assert.True(keep);
            Assert.Equal(("GAA", "A1", "KR", "85"), (name.DESIGNATION_PLANT.Text, name.DESIGNATION_LOCATION.Text, name.FUNC_CODE.Text, name.FUNC_COUNTER.Text));
        };
        try
        {
            new EplanDevicePlacer().Place("project", page.Name, part, "1", 80, 160, tag);

            Assert.Equal(expectedRenames, renames);
            Assert.All(functions, function => Assert.Equal(1, function.NameWrites));
        }
        finally
        {
            EplanProjects.Project = null;
            DeviceService.Create = null;
            NameService.Rename = null;
        }
    }

    [Fact]
    public void ReadCommitted_ReacquiresRenamedFunctionsAfterTheirIdsChange()
    {
        var page = new Page { Name = "=GAA+A1&EFS1/85" };
        const string tag = "=GAA+A1-KR85";
        var before = new Function(1) { Name = tag + ":1", IsMainFunction = true, Page = page, FunctionCategory = FunctionCategory.PLCTerminal };
        page.AllPlacements = [before];
        var project = new Project(page);
        EplanProjects.Project = project;
        DeviceService.Create = (_, _, _, _) => [before];
        try
        {
            var placer = new EplanDevicePlacer();
            PlacedDevice result = placer.Place("project", page.Name, "SIE.6ES7131-4BD01-0AA0", "1", 80, 160, "");
            page.AllPlacements = [new Function(100) { Name = tag + ":1", IsMainFunction = true, Page = page, FunctionCategory = FunctionCategory.PLCTerminal }];
            project.UnplacedFunctions = [.. _suffixes.Skip(1).Select(suffix => new Function(100 + suffix) { Name = tag + ":" + suffix, FunctionCategory = FunctionCategory.PLCTerminal })];
            before.CheckNative = () => throw new InvalidOperationException("Transaction wrapper was reused after commit.");

            placer.ReadCommitted("project", result);

            Assert.Equal(6, result.Names.Count);
            Assert.Equal(5, result.Unplaced);
            Assert.Equal([.. _suffixes.Skip(1).Select(suffix => tag + ":" + suffix)], result.UnplacedFunctions);
            Assert.DoesNotContain("=+-K3", result.Names);
        }
        finally
        {
            EplanProjects.Project = null;
            DeviceService.Create = null;
        }
    }
}
