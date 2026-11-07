using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
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
public sealed partial class MacroNativeAdapterTests : IDisposable
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
    [InlineData(0, "#<EES_F_CH1>")]
    [InlineData(2, "#<EES_F_CH2>")]
    public void MacroReader_ReadsOnlyTheSelectedVariantsPlaceholderMetadata(int variant, string name)
    {
        Page page = ConfigureMacro();
        var first = new PlaceHolder { Name = "#<EES_F_CH1>", Page = page };
        first.Records.AddRange(["3A", "6A"]);
        first.Variables.Add("Current");
        var second = new PlaceHolder { Name = "#<EES_F_CH2>", Page = page };
        second.Records.AddRange(["4A", "10A"]);
        second.Variables.Add("Label");
        WindowMacro.State!.Representations[WindowMacro.Enums.RepresentationType.Overview][0] = [first];
        WindowMacro.State.Representations[WindowMacro.Enums.RepresentationType.Overview][2] = [second];

        MacroContent content = new EplanMacroReader().ReadMacro("project", "module.ema", representation: "Overview", variant: variant);

        MacroPlaceholderRecord placeholder = Assert.Single(content.Placeholders);
        Assert.Equal(name, placeholder.Name);
        Assert.Equal(variant == 0 ? ["3A", "6A"] : ["4A", "10A"], placeholder.ValueSets);
        Assert.Equal(variant == 0 ? ["Current"] : ["Label"], placeholder.Variables);
        Assert.Equal(0, first.CurrentValueReads);
        Assert.Equal(0, second.CurrentValueReads);
    }

    [Fact]
    public void MacroReader_UnreadablePlaceholdersAreSkippedAndTheOthersRemainAvailable()
    {
        Page page = ConfigureMacro();
        var invalidName = new PlaceHolder { Page = page, NameError = new BaseException("Name unavailable") };
        var invalidRecords = new PlaceHolder { Name = "unreadable", Page = page, RecordError = new BaseException("Records unavailable") };
        invalidRecords.Records.Add("6A");
        var readable = new PlaceHolder { Name = "#<EES_F_CH1>", Page = page };
        readable.Records.Add("6A");
        WindowMacro.State!.Representations[WindowMacro.Enums.RepresentationType.Overview][0] = [invalidName, readable, invalidRecords];

        MacroContent content = new EplanMacroReader().ReadMacro("project", "module.ema", representation: "Overview");

        Assert.Equal("#<EES_F_CH1>", Assert.Single(content.Placeholders).Name);
        Assert.Equal(3, content.ObjectCount);
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

    [Fact]
    public void Pxc2861289_OverviewAtZero292_EmptyBlockReadFailureKeepsInsertedObjects()
    {
        Page page = ConfigureMacro();
        page.Name = "=K1+A2&EFA1/5";
        var empty = new Block { Page = page, NameError = new BaseException("访问一对象时出错") };
        var plc = Function(page, 20);
        WindowMacro.State!.Representations[WindowMacro.Enums.RepresentationType.Overview][0] = [empty, plc];
        NativeInsert.Place = target => { target.AllPlacements = [empty, plc]; return [empty, plc]; };
        var request = new MacroInsertArgs
        { Project = "project", Page = page.Name, Part = "PXC.2861289", PartVariant = "1", Representation = "Overview", Variant = 0, X = 0, Y = 292, MacroBox = false };
        var transactions = new FakeTransactions();
        TransactionRunner.Run(transactions, "eplan_macro_insert", () =>
        {
            PlacedMacro placed = new EplanMacroInserter().Insert(request.Project!, request.Page!, "PXC/2861289.ema", request.Variant ?? 0,
                request.X ?? 0, request.Y ?? 0, true, request.Representation!, request.Part, request.PartVariant, request.MacroBox ?? true);
            Assert.Equal(2, placed.ObjectCount);
            PageDrawingGeometry drawing = new EplanDrawingReader().Read(request.Project!, request.Page!)!;
            DrawingPlacement block = Assert.Single(drawing.Placements, p => p.Kind == "Block");
            Assert.False(block.Properties.ContainsKey("name"));
            Assert.Contains("unavailable", block.Properties["read_warning"], StringComparison.Ordinal);
            Assert.Single(drawing.Placements, p => p.Kind == "Function");
            return placed;
        });
        Assert.Contains("Commit", transactions.Calls);
        Assert.DoesNotContain("Rollback", transactions.Calls);
    }

    [Fact]
    public void Values_OnlyTheInsertedPlaceholderReceivesItsExactRecord()
    {
        Page page = ConfigureMacro();
        var existing = new PlaceHolder(100) { Page = page, Name = "#<EES_F_CH1>" };
        var inserted = new PlaceHolder(101) { Page = page, Name = "#<EES_F_CH1>" };
        var otherCase = new PlaceHolder(102) { Page = page, Name = "#<ees_f_ch1>" };
        page.AllPlacements = [existing];
        NativeInsert.Place = target =>
        {
            target.AllPlacements = [existing, inserted, otherCase];
            return [inserted, otherCase];
        };
        var transactions = new FakeTransactions();
        inserted.Applying = valueSet =>
        {
            Assert.Equal("6A", valueSet);
            Assert.Contains("Begin AI: eplan_macro_insert", transactions.Calls);
            Assert.DoesNotContain("Commit", transactions.Calls);
        };

        TransactionRunner.Run(transactions, "eplan_macro_insert", () => new EplanMacroInserter().Insert(
            "project", page.Name, "Selective.ema", 0, 20, 296, true, "Overview",
            values: new Dictionary<string, string> { ["#<EES_F_CH1>"] = "6A" }));

        Assert.Equal(["6A"], inserted.AppliedRecords);
        Assert.Empty(existing.AppliedRecords);
        Assert.Empty(otherCase.AppliedRecords);
        Assert.Contains("Commit", transactions.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Values_InsertedPlaceholderCountMismatchIsOperationFailedAndRolledBack(int count)
    {
        Page page = ConfigureMacro();
        Placement[] inserted = [.. Enumerable.Range(0, count).Select(i => new PlaceHolder(100 + i)
        {
            Page = page,
            Name = "#<EES_F_CH1>",
        })];
        NativeInsert.Place = target =>
        {
            target.AllPlacements = inserted;
            return inserted;
        };
        var transactions = new FakeTransactions { Snapshot = () => () => page.AllPlacements = [] };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(transactions, "eplan_macro_insert",
            () => new EplanMacroInserter().Insert("project", page.Name, "Selective.ema", 0, 20, 296, true, "Overview",
                values: new Dictionary<string, string> { ["#<EES_F_CH1>"] = "6A" }))).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Empty(page.AllPlacements);
        Assert.Contains("Rollback", transactions.Calls);
        Assert.DoesNotContain("Commit", transactions.Calls);
    }

    [Fact]
    public void Values_ApplyFailureRollsBackEarlierRecordsAndInsertedObjects()
    {
        Page page = ConfigureMacro();
        var first = new PlaceHolder(100) { Page = page, Name = "#<EES_F_CH1>" };
        var failing = new PlaceHolder(101) { Page = page, Name = "#<EES_F_CH2>", ApplyError = new BaseException("Cannot apply record") };
        NativeInsert.Place = target =>
        {
            target.AllPlacements = [first, failing];
            return [first, failing];
        };
        var transactions = new FakeTransactions
        {
            Snapshot = () => () =>
            {
                page.AllPlacements = [];
                first.AppliedRecords.Clear();
            },
        };
        var values = new Dictionary<string, string> { ["#<EES_F_CH1>"] = "6A", ["#<EES_F_CH2>"] = "3A" };

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(transactions, "eplan_macro_insert",
            () => new EplanMacroInserter().Insert("project", page.Name, "Selective.ema", 0, 20, 296, true, "Overview", values: values)));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Same(failing.ApplyError, error.InnerException);
        Assert.Empty(page.AllPlacements);
        Assert.Empty(first.AppliedRecords);
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
