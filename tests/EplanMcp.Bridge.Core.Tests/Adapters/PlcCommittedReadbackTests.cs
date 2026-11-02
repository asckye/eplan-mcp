using System.Reflection;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class PlcCommittedReadbackTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "renamed")]
    [InlineData(true, "missing")]
    [InlineData(true, "position")]
    [InlineData(true, "parent")]
    [InlineData(true, "reference")]
    [InlineData(true, "native_reference")]
    [InlineData(true, "plug")]
    [InlineData(true, "address")]
    [InlineData(true, "symbolic_address")]
    [InlineData(true, "text")]
    [InlineData(true, "ownership")]
    [InlineData(true, "foreign_card")]
    [InlineData(true, "card_id")]
    [InlineData(true, "not_found")]
    [InlineData(true, "page")]
    [InlineData(true, "main")]
    public void D3_AfterCommitAndDispose_ReacquiresByIdWithoutTouchingTransactionHandles(bool displays, string? fault)
    {
        const string Card = "=GAA+A1-D3KF5";
        var page = new Page { Name = "=GAA+A1&EFS1/5" };
        var cardPage = new Page { Name = "=GAA+A1&EFS1/6" };
        var unrelatedPage = new Page { Name = "+UNRELATED/1", FindPlacements = () => throw new InvalidOperationException("Unrelated page scan.") };
        var args = new DevicePlaceArgs
        {
            Tag = Card,
            Page = page.Name,
            Library = "IEC_symbol",
            Symbol = "PLC_CBOX",
            SymbolVariant = 0,
            PlcConnectionPoint = "1",
            PlcAddress = "I0.1",
            PlcSymbolicAddress = "FICTIONAL_DI_1",
            PlcText = "Fictional D3 input",
            PlcPlugDesignation = displays ? "-X10" : null,
            X = 120,
            Y = 176,
            DisplayProperties = displays ? [new() { PropertyId = 20213, OffsetXmm = 4, OffsetYmm = 12 }, new() { PropertyId = 20300, OffsetXmm = 4, OffsetYmm = 8 }, new() { PropertyId = 20406, OffsetXmm = 4, OffsetYmm = 4 }, new() { PropertyId = 20400, OffsetXmm = 4 }] : null
        };
        bool ended = false;
        int staleTouches = 0;
        void Guard()
        {
            if (ended)
            { staleTouches++; throw new ObjectDisposedException("Transaction-bound native handle"); }
        }
#pragma warning disable CA2000 // A39/A55: fake persistent objects are borrowed; disposing them must fail.
        var originalCard = new PLC { Name = Card, Page = cardPage, IsMainFunction = true, FunctionCategory = FunctionCategory.PLCBox, CheckNative = Guard };
        var point = new Terminal(fault == "not_found" ? 30 : 3)
        {
            Name = fault == "renamed" ? "+A1-D3KF6:1" : Card + ":1",
            Page = page,
            Location = new(120, 176),
            FunctionCategory = FunctionCategory.PLCTerminal
        };
        point.Properties.FUNC_PLCADDRESS = args.PlcAddress!;
        point.Properties.FUNC_PLCSYMBOLICADDRESS_MANUAL = args.PlcSymbolicAddress!;
        point.Properties.FUNC_TEXT = args.PlcText!;
        point.Properties.FUNC_PLCPLUG_DESIGNATION_MANUAL = args.PlcPlugDesignation ?? "";
        var card = new PLC(fault == "card_id" ? 40 : 4)
        {
            Name = fault == "renamed" ? "+A1-D3KF6" : Card,
            Page = cardPage,
            IsMainFunction = true,
            FunctionCategory = FunctionCategory.PLCBox,
            PLCTerminals = [point]
        };
        // Same-name decoys prove both lookups use identity rather than the requested names or first match.
        var decoyPoint = new Terminal(33) { Name = Card + ":1", Page = page };
        var decoyCard = new PLC(44) { Name = Card, IsMainFunction = true, FunctionCategory = FunctionCategory.PLCBox };
        point.PropertyPlacements = [.. (args.DisplayProperties ?? []).Select((d, i) => new PropertyPlacement(100 + i, point)
        {
            DisplayedId = d.PropertyId, Location = new(d.OffsetXmm, d.OffsetYmm), Height = 2.5,
            Display = d.PropertyId switch { 20213 => fault == "renamed" ? "-D3KF6" : "-D3KF5", 20406 => "-X10", 20400 => "I0.1", _ => "" }
        })];
        var project = new Project(page, cardPage, unrelatedPage) { RejectUnfilteredFunctions = true, OmitTerminalsFromFunctions = true };
        project.SymbolLibraries = [new() { Name = args.Library!, Symbols = [new() { Name = args.Symbol!, Variants = [new()] }] }];
        project.FunctionDefinitionLibrary.Definitions = [new()];
        project.FindFunctions = filter =>
        {
            Assert.Equal(FunctionCategory.PLCBox, filter?.FunctionCategory);
            if (!ended)
            { return [originalCard]; }
            Assert.Null(filter!.Name);
            return [decoyCard, card];
        };
#pragma warning restore CA2000
        EplanProjects.Project = project;
        FunctionPropertyList? originalProperties = null;
        Terminal.OnCreate = terminal => { terminal.CheckNative = Guard; originalProperties = terminal.Properties; };
        project.EndTransaction = () =>
        {
            Assert.True(originalProperties!.Disposed); // Every opened list must be released before Commit/Dispose.
            ended = true;
            page.AllPlacements = [decoyPoint, point];
        };
        var adapter = new EplanDeviceSymbolPlacement("project", page.Name, new()
        {
            Library = args.Library!,
            Name = args.Symbol!,
            Variant = 0,
            FunctionCategory = "PLCTerminal",
            PlcInputDefinition = "1/2"
        });
        DevicePlaceData data;
        try
        {
            data = TransactionRunner.Run(adapter, "eplan_device_place", () =>
            {
                DevicePlaceData placed = adapter.Place(args, Card + ":1");
                Assert.Equal(displays ? 4 : 0, adapter.ReadDisplays().Count);
                return placed;
            }, _ => true).Result;
        }
        finally
        { Terminal.OnCreate = null; }
        Assert.True(ended);
        Assert.Equal(0, staleTouches);
        AssertNoNativeFields(adapter);

        switch (fault)
        {
            case "missing":
                point.PropertyPlacements = point.PropertyPlacements[1..];
                break;
            case "position":
                point.PropertyPlacements[0].Location = new(4, 16);
                break;
            case "parent":
                point.PropertyPlacements[0] = new(100, decoyPoint) { DisplayedId = 20213, Location = new(4, 12), Height = 2.5, Display = "-D3KF5" };
                break;
            case "reference":
                point.PropertyPlacements[1].Display = "&EFS1/6.2";
                break;
            case "native_reference":
                point.Properties.FUNC_CROSSREFERENCE = "&EFS1/6.2";
                point.PropertyPlacements[1].Display = "&EFS1/6.2";
                break;
            case "plug":
                point.Properties.FUNC_PLCPLUG_DESIGNATION_MANUAL = "-WRONG";
                break;
            case "address":
                point.Properties.FUNC_PLCADDRESS = "I9.9";
                break;
            case "symbolic_address":
                point.Properties.FUNC_PLCSYMBOLICADDRESS_MANUAL = "WRONG";
                break;
            case "text":
                point.Properties.FUNC_TEXT = "Wrong text";
                break;
            case "ownership":
                card.PLCTerminals = [];
                break;
            case "foreign_card":
                point.PLC = decoyCard;
                break;
            case "page":
                point.Page = cardPage;
                break;
            case "main":
                point.IsMainFunction = true;
                break;
        }
        bool success = fault is null or "renamed" or "native_reference";
        if (success)
        {
            adapter.ReadCommitted(args, data);
            Assert.Equal((point.Name, card.Name, "I0.1", "FICTIONAL_DI_1", "Fictional D3 input"),
                (data.Plc!.Name, data.Plc.Owner, data.Plc.Address, data.Plc.ManualSymbolicAddress, data.Plc.Text));
            Assert.Equal(fault == "native_reference" ? "&EFS1/6.2" : null, data.Plc.MainReference);
            Assert.Equal(point.Name, data.Tag);
            Assert.Equal(args.PlcPlugDesignation, data.Plc.PlugDesignation);
        }
        else
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(() => adapter.ReadCommitted(args, data)).Error;
            Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.Committed), (error.Reason, error.Execution));
            string stage = fault switch
            {
                "ownership" or "foreign_card" or "card_id" => "ownership",
                "not_found" or "page" or "main" => "identity",
                "plug" or "address" or "symbolic_address" or "text" => "address, manual symbolic address, function text, plug designation",
                _ => "displays"
            };
            Assert.Contains(stage, error.Message, StringComparison.Ordinal);
        }
        Assert.Equal(0, staleTouches);
        AssertNoNativeFields(adapter);
        Assert.Equal(fault is not ("not_found" or "page" or "main" or "ownership" or "foreign_card" or "card_id"), point.Properties.Disposed);
        Assert.All(project.FunctionQueries, query => Assert.Equal(FunctionCategory.PLCBox, query.Category));
        Assert.Equal(fault is "not_found" or "page" or "main" ? 1 : 2, project.FunctionQueries.Count);
    }

    private static void AssertNoNativeFields(EplanDeviceSymbolPlacement adapter) =>
        Assert.DoesNotContain(adapter.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.GetValue(adapter)?.GetType().Namespace?.StartsWith("Eplan.EplApi.", StringComparison.Ordinal) == true);
}
