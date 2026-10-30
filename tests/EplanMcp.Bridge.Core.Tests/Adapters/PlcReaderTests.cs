using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class PlcReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadPlc_BoundsRepresentationQueriesToTheCardAndPreservesCrossPageAndUnplacedObjects(bool detailed)
    {
#pragma warning disable CA2000 // The linked reader borrows fake project-owned objects, whose Dispose rejects ownership mistakes.
        var overview = new Page { Name = "+A1/1" };
        var circuit = new Page { Name = "+A1/2" };
        var plc = new PLC { Name = "+A1-KF1", Page = overview, IsMainFunction = true, FunctionCategory = FunctionCategory.PLCBox };
        var channel = Point(plc.Name + ":1", circuit, plc);
        channel.Properties.FUNC_PLCADDRESS = "??_??@I0.0;";
        channel.Properties.FUNC_PLCSYMBOLICADDRESS_MANUAL = "??_??@Start;";
        channel.Properties.FUNC_TEXT = "??_??@Start input;";
        plc.PLCTerminals = [channel, Point(channel.Name, overview, plc)];
        overview.AllPlacements = [plc, Point(channel.Name, overview)];
        circuit.AllPlacements = [channel, Point("+A1-KF10:1", circuit)];
        var project = new Project(overview, circuit) { RejectUnfilteredFunctions = true, UnplacedFunctions = [Point(channel.Name, null)] };
#pragma warning restore CA2000
        EplanProjects.Project = project;

        PlcRecord record = new EplanPlcReader().ReadPlc("project", plc.Name, "??_??", detailed)!;

        Assert.Equal(2, record.Channels.Count);
        Assert.All(record.Channels, c => Assert.Equal(channel.Name, c.Name));
        Assert.Equal(("I0.0", "Start input"), (record.Channels[0].Address, record.Channels[0].Text));
        Assert.Equal(detailed ? plc.Name : null, record.Channels[0].Owner);
        Assert.Equal(detailed ? "Start" : null, record.Channels[0].ManualSymbolicAddress);
        Assert.All(record.Channels, c => Assert.Equal(detailed ? (int?)2 : null, c.ParentlessRepresentations));
        Assert.Equal(detailed ? 2 : 1, project.FunctionQueries.Count);
        if (detailed)
        { Assert.Equal((plc.Name + ":", false, (FunctionCategory?)FunctionCategory.PLCTerminal), project.FunctionQueries[1]); }
    }

#pragma warning disable CA2000 // Ownership transfers to the fake project.
    private static Terminal Point(string name, Page? page, PLC? owner = null) => new() { Name = name, Page = page!, PLC = owner, FunctionCategory = FunctionCategory.PLCTerminal };
#pragma warning restore CA2000
}
