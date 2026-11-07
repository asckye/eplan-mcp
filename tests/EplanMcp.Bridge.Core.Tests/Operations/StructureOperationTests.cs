using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>The structure.* operations against a fake project.</summary>
[Trait("Size", "Small")]
public sealed class StructureOperationTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true });
    private readonly Identifiers _identifiers = new();
    private readonly FakeTransactions _transactions = new();
    private readonly FakePageReader _pages = new();

    [Fact]
    public void List_FiltersByBlockAndUse_TellingSingleFromDoublePrefixes()
    {
        Assert.Equal(["+S1", "+OLD"], List(new StructureListArgs { Block = "+", Limit = 10 }).Items.Select(i => i.Name));
        Assert.Equal(["++SITE"], List(new StructureListArgs { Block = "++", Limit = 10 }).Items.Select(i => i.Name));
        Assert.Equal(["+OLD"], List(new StructureListArgs { UnusedOnly = true, Limit = 10 }).Items.Select(i => i.Name));
    }

    [Fact]
    public void UpdatePlan_ListsCreateAndUpdate_SkipsTheSame_WritesNothing()
    {
        WritePlan plan = UpdatePlan(Update(("+S1", "en_US@Cabinet;zh_CN@柜;"), ("&EFS", "en_US@New;"), ("+F1", "en_US@Field;"), ("+OLD", null)));
        Assert.Equal([("update", "&EFS", "en_US@Old;"), ("create", "+F1", (string?)null)], plan.Changes.Select(c => (c.Action, c.Name!, c.OldValue)));
        Assert.Empty(_identifiers.Calls);
    }

    [Fact]
    public void UpdateExecute_CreatesThenDescribes_ReadsBack_CommitsOnce()
    {
        StructureUpdateArgs args = Update(("+F1", "en_US@Field;"), ("&EFS", "en_US@New;"));
        args.Fingerprint = UpdatePlan(args).Fingerprint;
        StructureUpdateData data = UpdateExecute(args);
        Assert.Equal(["create +F1", "describe +F1", "describe &EFS"], _identifiers.Calls);
        Assert.Equal(["+F1", "&EFS"], data.Identifiers.Select(i => i.Name));
        Assert.Equal(1, _transactions.Calls.Count(c => c == "Commit"));
    }

    [Fact]
    public void UpdateExecute_DifferentReadBack_RollsBack()
    {
        StructureUpdateArgs args = Update(("&EFS", "en_US@New;"));
        args.Fingerprint = UpdatePlan(args).Fingerprint;
        _identifiers.StoreDescription = _ => "en_US@Something else;";
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => UpdateExecute(args));
        Assert.Equal(ErrorReason.EplanOperationFailed, error.Error.Reason);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void UpdateExecute_ChangedSinceThePlan_IsStale()
    {
        StructureUpdateArgs args = Update(("&EFS", "en_US@New;"));
        args.Fingerprint = UpdatePlan(args).Fingerprint;
        _identifiers.Stored.Single(i => i.Name == "&EFS").Description = "en_US@Changed by the engineer;";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => UpdateExecute(args)).Error.Reason);
        Assert.Empty(_identifiers.Calls);
    }

    [Theory]
    [InlineData("S1")]
    [InlineData("+")]
    [InlineData("+A B")]
    [InlineData("+A+B")]
    [InlineData("=A/1")]
    [InlineData("&-K1")]
    public void Update_BadName_IsInvalid(string name)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => UpdatePlan(Update((name, "x"))));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
    }

    [Fact]
    public void Update_SameNameTwice_IsInvalid()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => UpdatePlan(Update(("+S1", "a"), ("+S1", "b"))));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
    }

    [Fact]
    public void Update_Order_SortsTheBlock_KeepingTheOthersBehind_AndReadsBack()
    {
        var args = new StructureUpdateArgs { Project = "Line1", Order = ["+OLD"] };
        WritePlan plan = UpdatePlan(args);
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("+", "order", "+S1 +OLD", "+OLD"), (change.Name, change.Property, change.OldValue, change.NewValue));
        args.Fingerprint = plan.Fingerprint;
        UpdateExecute(args);
        Assert.Equal(["+OLD", "+S1"], _identifiers.Stored.Select(i => i.Name).Where(n => StructureNames.Prefix(n) == "+"));
    }

    [Fact]
    public void Update_OrderAlreadyThere_PlansNothing()
    {
        Assert.Empty(UpdatePlan(new StructureUpdateArgs { Project = "Line1", Order = ["+S1"] }).Changes);
    }

    [Fact]
    public void Update_OrderOfAnIdentifierNobodyHas_IsInvalid()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => UpdatePlan(new StructureUpdateArgs { Project = "Line1", Order = ["+NOTHERE"] }));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
    }

    [Fact]
    public void Update_OrderMayNameOneThisCallCreates()
    {
        StructureUpdateArgs args = Update(("+NEW", null));
        args.Order = ["+NEW", "+S1"];
        args.Fingerprint = UpdatePlan(args).Fingerprint;
        UpdateExecute(args);
        Assert.Equal(["+NEW", "+S1", "+OLD"], _identifiers.Stored.Select(i => i.Name).Where(n => StructureNames.Prefix(n) == "+"));
    }

    [Fact]
    public void Update_Translate_FillsOneLanguageDescriptionsOnly()
    {
        StructureUpdateArgs args = Update(("&EFS", "Plan"), ("+F1", "en_US@Field;"));
        args.Translate = true;
        Assert.Equal(["zh_CN@Plan;en_US@Plan;", "en_US@Field;"], UpdatePlan(args).Changes.Select(c => c.NewValue));
    }

    [Fact]
    public void Rename_RenamesReadsBackAndCommitsOnce()
    {
        var args = new StructureRenameArgs { Project = "Line1", Renames = [new() { Name = "+S1", NewName = "+CAB" }], Backup = Backup() };
        args.Fingerprint = RenamePlan(args).Fingerprint;
        StructureRenameData data = RenameExecute(args);
        Assert.Equal(["+CAB"], data.Identifiers.Select(i => i.Name));
        Assert.Equal(1, _transactions.Calls.Count(c => c == "Commit"));
    }

    [Theory]
    [InlineData("+S1", "&S1")]
    [InlineData("+S1", "+OLD")]
    [InlineData("+NOTHERE", "+X")]
    [InlineData("+A.B", "+C.B")]
    public void Rename_OtherBlockTakenUnknownOrOtherParent_IsInvalid(string name, string newName)
    {
        var args = new StructureRenameArgs { Project = "Line1", Renames = [new() { Name = name, NewName = newName }] };
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => RenamePlan(args)).Error.Reason);
    }

    [Fact]
    public void Delete_WithoutNames_TakesEveryUnused()
    {
        WritePlan plan = DeletePlan(new StructureDeleteArgs { Project = "Line1" });
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("delete", "+OLD"), (change.Action, change.Name!));
    }

    [Theory]
    [InlineData("+S1")]
    [InlineData("+NOTHERE")]
    public void Delete_UsedOrUnknownName_IsInvalid(string name)
    {
        var args = new StructureDeleteArgs { Project = "Line1", Identifiers = [name] };
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => DeletePlan(args));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
    }

    [Fact]
    public void DeleteExecute_KeptByEplan_RollsBack()
    {
        var args = new StructureDeleteArgs { Project = "Line1", Identifiers = ["+OLD"], Backup = Backup() };
        args.Fingerprint = DeletePlan(args).Fingerprint;
        _identifiers.KeepRemoved = true;
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => DeleteExecute(args));
        Assert.Equal(ErrorReason.EplanOperationFailed, error.Error.Reason);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void DeleteExecute_BacksUpFirst_Removes_CommitsOnce()
    {
        var args = new StructureDeleteArgs { Project = "Line1", Backup = Backup() };
        args.Fingerprint = DeletePlan(args).Fingerprint;
        StructureDeleteData data = DeleteExecute(args);
        Assert.Equal(["+OLD"], data.Identifiers.Select(i => i.Name));
        Assert.DoesNotContain(_identifiers.Stored, i => i.Name == "+OLD");
        Assert.NotNull(data.Backup);
        Assert.Equal(1, _transactions.Calls.Count(c => c == "Commit"));
    }

    private static BackupTarget Backup() => new() { Path = Path.Combine(Directory.CreateTempSubdirectory("eplanmcp-structure-").FullName, "Line1.elk"), MaxSeconds = 60 };

    private static StructureUpdateArgs Update(params (string Name, string? Description)[] items) => new()
    {
        Project = "Line1",
        Identifiers = [.. items.Select(i => new StructureIdentifierValue { Name = i.Name, Description = i.Description })],
    };

    private ListData<StructureIdentifier> List(StructureListArgs args) => (ListData<StructureIdentifier>)new StructureListOperation(_catalog, _identifiers).Execute(args)!;

    private WritePlan UpdatePlan(StructureUpdateArgs args) => (WritePlan)new StructureUpdatePlanOperation(_catalog, _identifiers, new FakeTranslator()).Execute(args)!;

    private WritePlan RenamePlan(StructureRenameArgs args) => (WritePlan)new StructureRenamePlanOperation(_catalog, _identifiers).Execute(args)!;

    private StructureRenameData RenameExecute(StructureRenameArgs args) =>
        (StructureRenameData)new StructureRenameExecuteOperation(_catalog, _identifiers, _pages, new FakeProjectBackup(_pages), _transactions).Execute(args)!;

    private WritePlan DeletePlan(StructureDeleteArgs args) => (WritePlan)new StructureDeletePlanOperation(_catalog, _identifiers).Execute(args)!;

    private StructureUpdateData UpdateExecute(StructureUpdateArgs args) => (StructureUpdateData)new StructureUpdateExecuteOperation(_catalog, _identifiers, new FakeTranslator(), _transactions).Execute(args)!;

    private StructureDeleteData DeleteExecute(StructureDeleteArgs args) =>
        (StructureDeleteData)new StructureDeleteExecuteOperation(_catalog, _identifiers, _pages, new FakeProjectBackup(_pages), _transactions).Execute(args)!;

    private sealed class Identifiers : IStructureIdentifiers
    {
        public List<StructureIdentifier> Stored { get; } =
        [
            new() { Name = "+S1", Description = "zh_CN@柜;en_US@Cabinet;", Used = true },
            new() { Name = "+OLD", Description = string.Empty, Used = false },
            new() { Name = "++SITE", Description = string.Empty, Used = true },
            new() { Name = "&EFS", Description = "en_US@Old;", Used = true },
        ];

        public List<string> Calls { get; } = [];

        public Func<string, string> StoreDescription { get; set; } = text => text;

        public bool KeepRemoved { get; set; }

        public IReadOnlyList<StructureIdentifier> Read(string projectPath) => [.. Stored];

        public void Create(string projectPath, string name)
        {
            Calls.Add("create " + name);
            Stored.Add(new StructureIdentifier { Name = name });
        }

        public void Describe(string projectPath, string name, string description)
        {
            Calls.Add("describe " + name);
            Stored.Single(i => i.Name == name).Description = StoreDescription(description);
        }

        public void Sort(string projectPath, IReadOnlyList<string> names)
        {
            Calls.Add("sort " + string.Join(" ", names));
            string prefix = StructureNames.Prefix(names[0]);
            List<StructureIdentifier> block = [.. Stored.Where(i => StructureNames.Prefix(i.Name) == prefix)];
            List<StructureIdentifier> sorted = [.. names.Select(n => block.Single(i => i.Name == n)), .. block.Where(i => !names.Contains(i.Name))];
            Stored.RemoveAll(block.Contains);
            Stored.InsertRange(0, sorted);
        }

        public void Rename(string projectPath, string name, string newName)
        {
            Calls.Add("rename " + name);
            Stored.Single(i => i.Name == name).Name = newName;
        }

        public void Remove(string projectPath, string name)
        {
            Calls.Add("remove " + name);
            if (!KeepRemoved)
            { Stored.RemoveAll(i => i.Name == name); }
        }
    }
}
