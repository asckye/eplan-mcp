using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using NativeInsert = Eplan.EplApi.HEServices.Insert;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Fake native objects are project-owned; their Dispose rejects borrowed-object disposal.

[Trait("Size", "Small")]
[Collection("Macro native adapters")]
public sealed class MacroNativeAdapterTests : IDisposable
{
    public void Dispose()
    {
        MDPartsManagement.Database = null;
        PathMap.MacroDirectory = null;
        WindowMacro.State = null;
        NativeInsert.Place = null;
        NativeInsert.Selection = null;
        EplanProjects.Project = null;
    }

    [Theory]
    [InlineData("1", null, "1")]
    [InlineData("1", "1", "1")]
    [InlineData("2", null, "2")]
    [InlineData("2", "2", "2")]
    public void GroupSymbolMacro_UsesDefaultUniqueOrExplicitPartVariantAndConvertsRootedPath(string variant, string? requested, string expected)
    {
        PathMap.MacroDirectory = Path.Combine(Path.GetTempPath(), "macro-root");
        MDPartsManagement.Database = new()
        {
            Parts = [new() { Variant = variant, Properties = new() { ARTICLE_GROUPSYMBOLMACRO = new("$(MD_MACROS)\\PXC\\2861344.ema") } }]
        };

        PartMacroSource source = new EplanMacroReader().ResolvePartMacro("PXC.2861344", requested);

        Assert.Equal(("PXC.2861344", expected, "PXC\\2861344.ema"), (source.Part, source.PartVariant, source.Macro));
        Assert.True(MDPartsManagement.Database.Closed);
        Assert.True(MDPartsManagement.Database.Disposed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    public void GroupSymbolMacro_AmbiguousOrMissingVariantIsRejectedAndDatabaseClosed(string? requested)
    {
        MDPartsManagement.Database = new() { Parts = [new() { Variant = "2" }, new() { Variant = "3" }] };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new EplanMacroReader().ResolvePartMacro("PXC.2861344", requested)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("2, 3", error.Message, StringComparison.Ordinal);
        Assert.True(MDPartsManagement.Database.Closed);
    }

    [Fact]
    public void GroupSymbolMacro_DefaultOneWinsAndOutsideRootIsRejected()
    {
        PathMap.MacroDirectory = Path.Combine(Path.GetTempPath(), "macro-root");
        var preferred = new MDPart { Properties = new() { ARTICLE_GROUPSYMBOLMACRO = new("PXC/2861344.ema") } };
        MDPartsManagement.Database = new() { Parts = [new() { Variant = "2" }, preferred] };
        Assert.Equal("1", new EplanMacroReader().ResolvePartMacro("PXC.2861344", null).PartVariant);
        preferred.Properties.ARTICLE_GROUPSYMBOLMACRO = new(PathMap.MacroDirectory + "-other\\outside.ema");
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => new EplanMacroReader().ResolvePartMacro("PXC.2861344", null)).Error.Reason);
    }

    [Fact]
    public void Pxc2861344_OverviewVariantZero_ReadsAndInsertsTheRequestedRepresentation()
    {
        Page page = ConfigureMacro();
        var reader = new EplanMacroReader();
        MDPartsManagement.Database = new() { Parts = [new() { Properties = new() { ARTICLE_GROUPSYMBOLMACRO = new("PXC/2861344.ema") } }] };
        PartMacroSource source = reader.ResolvePartMacro("PXC.2861344", "1");
        Assert.Equal("PXC/2861344.ema", source.Macro);
        MacroContent content = reader.ReadMacro("project", source.Macro, representation: "Overview", variant: 0);

        Assert.Equal(("Overview", 0, 2), (content.Representation, content.Variant, content.ObjectCount));
        Assert.Equal((WindowMacro.Enums.RepresentationType.Overview, 0), Assert.Single(WindowMacro.State!.Selections));
        NativeInsert.Place = target => { Assert.Same(page, target); target.AllPlacements = WindowMacro.State.Representations[WindowMacro.Enums.RepresentationType.Overview][0]; return [.. target.AllPlacements]; };
        PlacedMacro placed = new EplanMacroInserter().Insert("project", page.Name, source.Macro, 0, 20, 296, true, "Overview", source.Part, source.PartVariant);
        Assert.Equal((WindowMacro.Enums.RepresentationType.Overview, 0), NativeInsert.Selection);
        MacroPartReference article = Assert.Single(placed.ArticleReferences);
        Assert.Equal(("PXC.2861344", "1", 1), (article.PartNumber, article.Variant, article.Count));
    }

    [Fact]
    public void PartAssignment_OnlyNewEmptyMainFunctionsReceiveOneReference_ThenFreshWrappersAreRead()
    {
        Page page = ConfigureMacro();
        var existing = Function(page, 1);
        var empty = Function(page, 2);
        var populated = Function(page, 3);
        populated.ArticleReferences = [new() { PartNr = "Existing.Part", VariantNr = "4", Count = 2 }];
        var auxiliary = Function(page, 4, main: false);
        page.AllPlacements = [existing];
        NativeInsert.Place = target => { target.AllPlacements = [existing, empty, populated, auxiliary]; return [empty, populated, auxiliary]; };
        var adapter = new EplanMacroInserter();

        PlacedMacro placed = adapter.Insert("project", page.Name, "2861344.ema", 0, 20, 296, true, "Overview", "PXC.2861344", "2");

        Assert.Equal(("PXC.2861344", "2", 1u), Assert.Single(empty.ArticleAdds));
        Assert.Empty(existing.ArticleAdds);
        Assert.Empty(populated.ArticleAdds);
        Assert.Empty(auxiliary.ArticleAdds);
        Assert.Equal(("Existing.Part", "4", 2), (populated.ArticleReferences[0].PartNr, populated.ArticleReferences[0].VariantNr, populated.ArticleReferences[0].Count));
        Function[] fresh = [Function(page, 2), Function(page, 3), Function(page, 4, main: false)];
        fresh[0].ArticleReferences = empty.ArticleReferences;
        fresh[1].ArticleReferences = populated.ArticleReferences;
        page.AllPlacements = [existing, .. fresh];
        foreach (Function old in new[] { empty, populated, auxiliary })
        { old.CheckNative = () => throw new InvalidOperationException("Expired transaction wrapper"); }
        PlacedMacro readback = adapter.ReadCommitted("project", page.Name, placed);
        Assert.Equal(3, readback.ObjectCount);
        Assert.Equal(["PXC.2861344", "Existing.Part"], readback.ArticleReferences.Select(a => a.PartNumber));
    }

    [Fact]
    public void ArticleAssignmentFailure_RollsBackInsertedObjectsAndEarlierAssignments()
    {
        Page page = ConfigureMacro();
        var first = Function(page, 2);
        var failing = Function(page, 3);
        failing.AddArticleError = new BaseException("Cannot add article");
        NativeInsert.Place = target => { target.AllPlacements = [first, failing]; return [first, failing]; };
        var transactions = new FakeTransactions { Snapshot = () => () => { page.AllPlacements = []; first.ArticleReferences = []; } };

        BaseException error = Assert.Throws<BaseException>(() => TransactionRunner.Run(transactions, "eplan_macro_insert",
            () => new EplanMacroInserter().Insert("project", page.Name, "2861344.ema", 0, 20, 296, true, "Overview", "PXC.2861344", "1")));

        Assert.Same(failing.AddArticleError, error);
        Assert.Equal(("PXC.2861344", "1", 1u), Assert.Single(first.ArticleAdds));
        Assert.Empty(page.AllPlacements);
        Assert.Empty(first.ArticleReferences);
        Assert.Contains("Rollback", transactions.Calls);
        Assert.DoesNotContain("Commit", transactions.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MacroBox_OnlyRequestedFramesAreRemoved_ReferencedObjectsSurvive(bool retain)
    {
        Page page = ConfigureMacro();
        Placement[] referenced = [.. Enumerable.Range(1, 195).Select(id => Function(page, id, main: id == 1))];
        var frame = new MacroBox(300) { Page = page, ReferencedObjects = referenced };
        NativeInsert.Place = target => { target.AllPlacements = [frame, .. referenced]; return [frame, .. referenced]; };
        WindowMacro.State!.Representations[WindowMacro.Enums.RepresentationType.Overview][0] = [frame, .. referenced];
        MacroContent content = new EplanMacroReader().ReadMacro("project", "2861344.ema", representation: "Overview");
        Assert.Equal(1, content.MacroBoxCount);
        PlacedMacro result = new EplanMacroInserter().Insert("project", page.Name, "2861344.ema", 0, 0, 292, true, "Overview", macroBox: retain);
        Assert.Equal(retain ? 196 : 195, result.ObjectCount);
        Assert.Equal(retain, frame.IsValid);
        Assert.Equal(retain, page.AllPlacements.Contains(frame));
        Assert.All(referenced, item => { Assert.True(item.IsValid); Assert.Contains(item, page.AllPlacements); });
    }

    [Fact]
    public void MacroBoxRemovalFailure_RollsBackTheWholeInsert()
    {
        Page page = ConfigureMacro();
        var frame = new MacroBox(300) { Page = page, RemoveError = new BaseException("Cannot remove frame") };
        var function = Function(page, 2);
        NativeInsert.Place = target => { target.AllPlacements = [frame, function]; return [frame, function]; };
        var transactions = new FakeTransactions { Snapshot = () => () => page.AllPlacements = [] };
        BaseException error = Assert.Throws<BaseException>(() => TransactionRunner.Run(transactions, "eplan_macro_insert",
            () => new EplanMacroInserter().Insert("project", page.Name, "2861344.ema", 0, 0, 292, true, "Overview", macroBox: false)));
        Assert.Same(frame.RemoveError, error);
        Assert.Empty(page.AllPlacements);
        Assert.Contains("Rollback", transactions.Calls);
        Assert.DoesNotContain("Commit", transactions.Calls);
    }

    private static Page ConfigureMacro()
    {
        var page = new Page { Name = "+A1/80", PageType = DocumentTypeManager.DocumentType.Overview };
        EplanProjects.Project = new(page);
        WindowMacro.State = new();
        WindowMacro.State.Representations[WindowMacro.Enums.RepresentationType.MultiLine] = new() { [0] = [] };
        WindowMacro.State.Representations[WindowMacro.Enums.RepresentationType.Overview] = new() { [0] = [Function(page, 20), Function(page, 21, main: false)] };
        return page;
    }

    private static Function Function(Page page, long id, bool main = true) => new(id)
    { Page = page, Name = $"+A1-A{id}", IsMainFunction = main, FunctionCategory = main ? FunctionCategory.PLCBox : FunctionCategory.PLCTerminal };
}

[CollectionDefinition("Macro native adapters", DisableParallelization = true)]
public sealed class MacroNativeAdapterSuite;
