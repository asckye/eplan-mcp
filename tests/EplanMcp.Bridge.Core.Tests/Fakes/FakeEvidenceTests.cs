using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Tests.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Fakes;

#pragma warning disable CA2000 // The fixture owns native placements and pages, whose Dispose deliberately fails.

[Trait("Size", "Small")]
public sealed class FakeEvidenceTests
{
    [Fact]
    public void FakeDeviceRenamer_ShownRetainsDifferentPlant_AsA45()
    {
        var fake = new FakeDeviceRenamer();
        fake.Functions.Add(("=F+A2-KF1", "1", true, "=GAA+A2/1", "-KF1"));
        Assert.Equal("=F-KF1", fake.ReadDevice("project", "=F+A2-KF1")!.VisibleName);
        Assert.Equal("=F-KF2", fake.Rename("project", "=F+A2-KF1", "=F+A2-KF2").VisibleName);
    }

    [Fact]
    public void FakeCheckRunner_WholeProjectRecheckAppendsSummaries_AsA20()
    {
        var fake = new FakeCheckRunner();
        fake.Found.AddRange(Enumerable.Range(1, 6).Select(id => new ProjectMessageRecord { Category = "Warning", Id = id }));
        fake.Found.AddRange(Enumerable.Range(1, 14).Select(id => new ProjectMessageRecord
        {
            Category = "Hint",
            Id = 999,
            Region = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }));

        fake.Run("project", null, "");
        Assert.Equal(20, fake.Messages.Count);
        fake.Run("project", null, "");
        Assert.Equal(34, fake.Messages.Count);
        Assert.Equal(6, fake.Messages.Count(message => message.Category == "Warning"));
        Assert.All(fake.Found.Where(message => message.Id == 999), hint =>
            Assert.Equal(2, fake.Messages.Count(message => message.Id == 999 && message.Region == hint.Region)));
    }

    [Fact]
    public void FakeDeviceRenumberer_PostNumerateChecksOnlyTheDeviceTag_AsA18()
    {
        var fake = new FakeDeviceRenumberer();
        fake.Functions.Add(new() { Name = "-XG1:?1" });
        fake.Functions.Add(new() { Name = "-?XG1:?1" });
        fake.Numbers["-XG1"] = "-XG3";
        fake.Numbers["-?XG1"] = "-XG2";
        fake.Renumber("project", null, true, null, 1, 1);
        Assert.Equal(["-XG1:?1", "-XG2:?1"], fake.Functions.Select(function => function.Name));
    }

    [Fact]
    public void FakeDeviceRenumberer_ValidatesReportedSchemeNames_AsTheSchemeDefault_IsInvalidArgumentAndRolledBack_WithS12sSchemesAsCandidates()
    {
        var fake = new FakeDeviceRenumberer();
        foreach (string scheme in new[] { "Kennbuchstabe + Zähler Fluidtechnik", "Kennbuchstabe + Zähler/Zubehör Fluidtechnik", "Kennbuchstabe Zähler", "Media_code", "Standard" })
        {
            fake.Renumber("project", null, true, scheme, 1, 1);
        }
        Assert.Throws<BridgeErrorException>(() => fake.Renumber("project", null, true, "Default", 1, 1));
    }

    [Fact]
    public void FakePageRenumberer_TakenSecondNameRefusesWholeSet_AsA20()
    {
        var pages = new FakePageReader();
        foreach (string name in new[] { "/90", "/91", "/210" })
        {
            pages.Pages.Add(new PageSummary { Name = name });
        }
        var fake = new FakePageRenumberer(pages);
        var error = Assert.Throws<BaseException>(() => fake.Renumber("project", ["/90", "/91"], 200, 10, false, "retain"));
        Assert.StartsWith("S029167", error.Message, StringComparison.Ordinal);
        Assert.Equal(["/90", "/91", "/210"], pages.Pages.Select(page => page.Name));
    }

    [Fact]
    public void Function_NewUnplacedDeviceFunctionGetsPersistentIdentifierOnCommit_AsA85()
    {
        var page = new Page();
        var placed = new Function(11) { Page = page };
        var unplaced = new Function(12);
        DeviceService.Create = (_, _, _, _) => [placed, unplaced];
        try
        {
            using var service = new DeviceService();
            using var transaction = new TransactionManager().CreateTransaction();
            service.CreateDevice("part", "1", page, default);
            long temporary = unplaced.ObjectIdentifier;
            Assert.EndsWith("/1", unplaced.ToStringIdentifier(), StringComparison.Ordinal);
            transaction.Commit();
            Assert.NotEqual(temporary, unplaced.ObjectIdentifier);
            Assert.EndsWith("/0", unplaced.ToStringIdentifier(), StringComparison.Ordinal);
            Assert.Equal("53/17/11/0", placed.ToStringIdentifier());
        }
        finally
        {
            DeviceService.Create = null;
        }
    }

    [Fact]
    public void Block_MembersChangeIdentifiersAndUseRelativeCoordinates_AsA88()
    {
        var first = new Line(11) { Location = new(24, 33) };
        var second = new Line(12) { Location = new(29, 38) };
        var outside = new Line(13) { Location = new(50, 51) };
        var page = new Page { AllPlacements = [first, second, outside] };
        var block = new Block { Location = new(20, 30) };
        block.Create(page, [first, second]);
        Assert.NotEqual(11, block.SubPlacements[0].ObjectIdentifier);
        Assert.NotEqual(12, block.SubPlacements[1].ObjectIdentifier);
        Assert.Equal([new PointD(4, 3), new PointD(9, 8)], block.SubPlacements.Select(member => member.Location));
        Assert.Equal((13L, new PointD(50, 51)), (outside.ObjectIdentifier, outside.Location));
    }

    [Fact]
    public void Block_ValidatesMembersAndCountsGraphics_AsA88()
    {
        var page = new Page();
        var block = new Block();
        var line = new Line();
        var symbol = new SymbolReference(42);
        Assert.NotNull(Record.Exception(() => block.Create(page, [line])));
        Assert.NotNull(Record.Exception(() => block.Create(page, [symbol, new SymbolReference(43)])));
        block.Create(page, [line, symbol]);
        Assert.Equal(1, block.Count);
        Assert.Equal(2, block.SubPlacements.Length);
    }

    [Fact]
    public void Function_CreatedInTransactionInvisibleToFinderUntilCommit_AsA13()
    {
        var page = new Page();
        var function = new Function();
        using var finder = new DMObjectsFinder(new Project(page));
        using var filter = new FunctionsFilter { Page = page };
        using var transaction = new TransactionManager().CreateTransaction();
        function.Create(page, new());
        Assert.Empty(finder.GetFunctions(null));
        Assert.Empty(finder.GetFunctions(filter));
        transaction.Commit();
        Assert.Same(function, Assert.Single(finder.GetFunctions(null)));
        Assert.Same(function, Assert.Single(finder.GetFunctions(filter)));
    }

    [Fact]
    public void DMObjectsFinder_PageNamePrefixIsCaseSensitive_AsA11()
    {
        var page = new Page { Name = "=GAA+A1/10" };
        using var finder = new DMObjectsFinder(new Project(page));
        using var filter = new PagesFilter { Name = "=GAA+A1/1" };
        Assert.Same(page, Assert.Single(finder.GetPages(filter)));
        filter.Name = "=gaa+A1/1";
        Assert.Empty(finder.GetPages(filter));
    }

    [Fact]
    public void FunctionBasePropertyList_EmptyPlantAssignmentThrows_AsA88()
    {
        using var parts = new FunctionBasePropertyList { DESIGNATION_PLANT = "F" };
        var error = Assert.Throws<EmptyPropertyException>(() => parts.DESIGNATION_PLANT = "");
        Assert.StartsWith("S063111", error.Message, StringComparison.Ordinal);
        Assert.Equal("F", parts.DESIGNATION_PLANT.Text);
    }

    [Fact]
    public void PropertyValue_EmptyTextConversionThrows_AsA7()
    {
        using var value = new PropertyValue("");
        Assert.True(value.IsEmpty);
        Assert.Throws<EmptyPropertyException>(() => value.ToString());
    }

    [Fact]
    public void UndoStep_DoUndoAfterCloseHasNoEffect_AsA7()
    {
        using var manager = new UndoManager();
        using var step = manager.CreateUndoStep();
        int before = FakeMoveNative.Value;
        FakeMoveNative.Value++;
        step.CloseUndo();
        step.DoUndo();
        Assert.Equal(before + 1, FakeMoveNative.Value);
        FakeMoveNative.UndoPrevious();
        Assert.Equal(before, FakeMoveNative.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FakeMoveNative_DisposeUncommittedRestoresPageProperty_AsA7(bool safety)
    {
        int before = FakeMoveNative.Value;
        using (IDisposable scope = safety ? SafetyPoint.Create() : new TransactionManager().CreateTransaction())
        {
            FakeMoveNative.Value++;
        }
        Assert.Equal(before, FakeMoveNative.Value);
    }

    [Fact]
    public void SafetyPoint_RollbackInsideUndoStepCannotRestore_AsA7()
    {
        using var manager = new UndoManager();
        using var step = manager.CreateUndoStep();
        int before = FakeMoveNative.Value;
        using (var point = SafetyPoint.Create())
        {
            FakeMoveNative.Value++;
            point.Rollback();
        }
        Assert.Equal(before + 1, FakeMoveNative.Value);
        step.DoUndo();
    }

    [Fact]
    public void Cable_DistinctCablesRetainDistinctObjectNumbers_AsA32()
    {
        var store = new FakeCableStore();
        var first = new Cable(store, new());
        var second = new Cable(store, new());
        first.Create(store.Page, new(), default, default);
        second.Create(store.Page, new(), default, default);
        Assert.NotEqual(first.ObjectIdentifier, second.ObjectIdentifier);
        Assert.Equal([first.ObjectIdentifier, second.ObjectIdentifier], store.FindPlacements().Select(p => p.ObjectIdentifier));
    }

    [Fact]
    public void Cable_BareTagStoresEmptyLocation_AsA55()
    {
        var store = new FakeCableStore();
        var cable = new Cable(store, new());
        cable.Create(store.Page, new(), default, default);
        cable.Name = "-W9";
        Assert.Equal("+-W9", cable.Name);
    }

    [Theory]
    [InlineData("+A1-W3", "-W3")]
    [InlineData("+A1-W4", ">-W4")]
    public void Cable_VisibleNameChangesOnAdjustment_AsA81(string name, string shown)
    {
        var store = new FakeCableStore();
        store.Page.Name = "=GAA+A1&EFS1/3";
        var cable = new Cable(store, new());
        cable.Create(store.Page, new(), default, default);
        cable.Name = name;
        Assert.Empty(cable.VisibleName);
        using var service = new NameService();
        service.AdjustVisibleName(store.Page, cable);
        Assert.Equal(shown, Assert.IsType<Cable>(Assert.Single(store.FindPlacements())).VisibleName);
    }

    [Fact]
    public void Cable_MembersIncludeOnlyItsAssignedWires_AsA64()
    {
        var store = new FakeCableStore();
        var assigned = new CableState();
        store.Wire.Cable = assigned;
        var otherBranch = new ConnectionState();
        store.Wires.Add(otherBranch);
        var cable = new Cable(store, assigned);
        Assert.Same(store.Wire, Assert.Single(cable.CableConnections).State);
        otherBranch.Cable = assigned;
        Assert.Equal([store.Wire, otherBranch], cable.CableConnections.Select(connection => connection.State));
    }

    [Fact]
    public void ConnectionPropertyList_UnallocatedCableNameIsBarePlus_AsA55()
    {
        var store = new FakeCableStore();
        using var list = Assert.Single(store.FindConnections()).Properties;
        using var name = list[(Properties.Connection)20005];
        Assert.Equal("+", name.ToString());
    }

    [Fact]
    public void ConnectionDefinitionPoint_CoreHasEmptyVisibleNameAfterWrite_AsA81()
    {
        var store = new FakeCableStore();
        store.Wire.Cable = new CableState { Name = "+A1-W3" };
        var point = Assert.Single(Assert.Single(store.FindConnections()).ConnectionDefPoints);
        point.VisibleName = "-W3";
        Assert.Empty(point.VisibleName);
    }

    [Fact]
    public void Connection3D_BothPinReadsThrow_AsA11()
    {
        Connection connection = new Connection3D();
        Assert.Throws<NotSupportedException>(() => connection.StartPin);
        Assert.Throws<NotSupportedException>(() => connection.EndPin);
    }

    [Fact]
    public void MultiLangString_IndependentTextHasNoConcreteLanguageEntry_AsA11()
    {
        using var value = new MultiLangString("??_??@text;");
        Assert.Equal("", value.GetString(ISOCode.Language.en_US));
        Assert.Equal("text", value.GetString(ISOCode.Language.L___));
    }
}
