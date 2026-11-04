using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DevicePartAssignmentTests
{
    private const string Tag = "=GAA+A1-SF91";
    private const string Part = "SIE.3SB3400-0B";
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1" });
    private readonly FakeDeviceReader _reader = new();
    private readonly FakeDeviceRenamer _renamer = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Store _store;

    public DevicePartAssignmentTests()
    {
        _renamer.Functions.Add((Tag, "1", true, "=GAA+A1&EFS1/1", "SF91"));
        _store = new Store(_renamer, _transactions);
        _store.Targets.Add(new DevicePartTarget { Id = "1", Name = Tag, Tag = Tag, VisibleName = "-SF91", Page = "=GAA+A1&EFS1/1" });
        _transactions.Snapshot = () =>
        {
            List<DevicePart> before = [.. _store.Targets[0].Parts];
            string name = _store.Targets[0].Name;
            var functions = _renamer.Functions.ToList();
            return () => { if (!_store.KeepPartAfterRollback) { _store.Targets[0].Parts = before; } _store.Targets[0].Name = _store.KeepNameAfterRollback ? "wrong" : name; _renamer.Functions.Clear(); _renamer.Functions.AddRange(functions); };
        };
    }

    [Theory]
    [InlineData(null, null, null, "At least one")]
    [InlineData("-SF99", null, "1", "requires 'part'")]
    [InlineData(null, " ", null, "nonempty part")]
    [InlineData(null, Part, " ", "nonempty")]
    [InlineData("bad", Part, null, "device part")]
    public void InvalidArgumentsAreRefusedBeforeReading(string? newTag, string? part, string? variant, string message)
    {
        var args = new DeviceUpdateArgs { Project = "Line1", Tag = Tag, NewTag = newTag ?? string.Empty, Part = part, Variant = variant };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;
        Assert.Contains(message, error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Equal(0, _store.Reads);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartOnlyPlansPlacedOrUnplacedMainWithSuffix(bool unplaced)
    {
        _renamer.Functions[0] = _renamer.Functions[0] with { Name = Tag + ":1" };
        _store.Targets[0].Name = Tag + ":1";
        _store.Targets[0].Page = unplaced ? null : "=GAA+A1&EFS1/1";
        WritePlan plan = Plan(Args());
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("add", "part", Tag, "1", Part), (change.Action, change.Kind, change.Name, change.Property, change.NewValue));
        Assert.Equal(Tag + ":1", plan.Details!["tag"]);
        Assert.Equal(Tag + ":1", plan.Details["previousTag"]);
        Assert.Empty(_store.Adds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MainCountMustBeExactlyOne(int count)
    {
        if (count == 0)
        { _store.Targets.Clear(); }
        else
        { _store.Targets.Add(new DevicePartTarget { Id = "2", Name = Tag, Tag = Tag }); }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;
        Assert.Contains($"found {count}", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void ShownTagResolvesUniqueDeviceTag()
    {
        DeviceUpdateArgs args = Args();
        args.Tag = "-SF91";
        Assert.Equal(Tag, Assert.Single(Plan(args).Changes).Name);
    }

    [Theory]
    [InlineData("missing", null, "PART_NOT_FOUND")]
    [InlineData(Part, "7", "INVALID_ARGUMENT")]
    public void MissingPartOrVariantIsRefused(string part, string? variant, string reason)
    {
        DeviceUpdateArgs args = Args();
        args.Part = part;
        args.Variant = variant;
        Assert.Equal(reason, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void UniqueCaseInsensitivePartIsCanonicalized()
    {
        DeviceUpdateArgs args = Args();
        args.Part = Part.ToLowerInvariant();
        Assert.Equal(Part, Assert.Single(Plan(args).Changes).NewValue);
        _store.Database.Parts.Add(new PartSearchItem { PartNumber = Part.ToLowerInvariant(), Variant = "1" });
        Assert.Equal(Part.ToLowerInvariant(), Assert.Single(Plan(args).Changes).NewValue);
        args.Part = "Sie.3SB3400-0B";
        Assert.Equal(ErrorReason.AmbiguousName, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void ExistingSamePartAndVariantHasNothingToChange()
    {
        _store.Targets[0].Parts.Add(new DevicePart { PartNumber = Part, Variant = "1", Count = 1 });
        Assert.Contains("nothing to change", Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingDifferentVariantDoesNotBlockAssignment()
    {
        _store.Targets[0].Parts.Add(new DevicePart { PartNumber = Part, Variant = "2", Count = 1 });
        Assert.Single(Plan(Args()).Changes);
    }

    [Fact]
    public void LegacyPlanStepWithoutAssignmentDependenciesRejectsPartInput()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new DeviceUpdatePlanOperation(_catalog, _reader, _renamer).Execute(Args())).Error;
        Assert.Contains("Use eplan_device_update", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultVariantIsOneEvenWhenOnlyAnotherVariantExists()
    {
        _store.Database.Parts[0].Variant = "2";
        Assert.Contains("variant '1'", Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error.Metadata!["violations"], StringComparison.Ordinal);
        DeviceUpdateArgs args = Args();
        args.Variant = "2";
        Assert.Equal("2", Assert.Single(Plan(args).Changes).Property);
    }

    [Fact]
    public void ChangedReferencesInvalidateThePlanWithoutWriting()
    {
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _store.Targets[0].Parts.Add(new DevicePart { PartNumber = "Existing", Variant = "1", Count = 2 });
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_store.Adds);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void ChangedMainNameInvalidatesThePlanWithoutWriting()
    {
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _store.Targets[0].Name = Tag + ":1";
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_store.Adds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartOnlyReturnsTheMainFullNameForDeviceGet(bool unplaced)
    {
        _store.Targets[0].Name = Tag + ":1";
        _store.Targets[0].Page = unplaced ? null : "=GAA+A1&EFS1/1";
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        DeviceUpdateData data = Execute(args);
        Assert.Equal((Tag + ":1", Tag + ":1", 1), (data.Tag, data.PreviousTag, data.Functions));
        if (unplaced)
        { Assert.Empty(data.Pages); }
        else
        { Assert.Equal("=GAA+A1&EFS1/1", Assert.Single(data.Pages)); }
        Assert.Equal(("1", Part, "1"), Assert.Single(_store.Adds));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignmentCommitsThenReadsFreshReferences(bool rename)
    {
        _store.Targets[0].Parts.Add(new DevicePart { PartNumber = "Existing", Variant = "1", Count = 3 });
        DeviceUpdateArgs args = Args();
        if (rename)
        { args.NewTag = "-SF92"; }
        args.Fingerprint = Plan(args).Fingerprint;
        DeviceUpdateData data = Execute(args);
        Assert.Equal((Part, "1", rename ? "=GAA+A1-SF92" : Tag, Tag), (data.Part, data.Variant, data.Tag, data.PreviousTag));
        Assert.Equal(("1", Part, "1"), Assert.Single(_store.Adds));
        Assert.True(_store.ReadAfterCommit);
        Assert.Equal(2, _store.Targets[0].Parts.Count);
        Assert.Equal(["Begin AI: eplan_device_update", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("part")]
    [InlineData("missing")]
    [InlineData("name")]
    public void FailedCommittedReadbackRollsBack(string mismatch)
    {
        _store.Mismatch = mismatch;
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Empty(_store.Targets[0].Parts);
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void FailedCompensationIsUnknownInsteadOfClaimingRollback()
    {
        _store.Mismatch = "part";
        _store.KeepPartAfterRollback = true;
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        Assert.Single(_store.Targets[0].Parts);
    }

    [Fact]
    public void FailedNameRestorationIsUnknownInsteadOfClaimingRollback()
    {
        _store.Mismatch = "name";
        _store.KeepNameAfterRollback = true;
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Fact]
    public void FailedPartReadbackAlsoRestoresTheRename()
    {
        _store.Mismatch = "part";
        DeviceUpdateArgs args = Args();
        args.NewTag = "-SF92";
        args.Fingerprint = Plan(args).Fingerprint;
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Execution);
        Assert.Equal(Tag, _renamer.Functions[0].Name);
        Assert.Empty(_store.Targets[0].Parts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactNonMainTerminalAcceptsPartOnlyAndPreservesIdentity(bool execute)
    {
        DeviceUpdateArgs args = TerminalArgs();
        WritePlan plan = Plan(args);
        Assert.Equal(("+A1-XD5:1", "PXC.3211813"), (Assert.Single(plan.Changes).Name, plan.Changes[0].NewValue));
        Assert.Empty(_store.Adds);
        if (!execute)
        { return; }
        args.Fingerprint = plan.Fingerprint;
        DeviceUpdateData actual = Execute(args);
        Assert.Equal(("+A1-XD5:1", "+A1-XD5:1", 1), (actual.Tag, actual.PreviousTag, actual.Functions));
        Assert.Equal(("1", "PXC.3211813", "1"), Assert.Single(_store.Adds));
        Assert.True(_store.ReadAfterCommit);
        Assert.Empty(_renamer.Renames);
        Assert.Equal(["Begin AI: eplan_device_update", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData("PLCTerminal")]
    [InlineData("BusTerminal")]
    [InlineData("NOContact")]
    public void OtherNonMainCategoriesCannotReceiveParts(string category)
    {
        DeviceUpdateArgs args = TerminalArgs();
        _store.Targets[0].Category = category;
        Assert.Throws<BridgeErrorException>(() => Plan(args));
        Assert.Empty(_store.Adds);
    }

    [Theory]
    [InlineData("+A1-XD5")]
    [InlineData("+A1-XD5:2")]
    [InlineData("+a1-xd5:1")]
    public void TerminalPartRequiresItsExactFullName(string tag)
    {
        DeviceUpdateArgs args = TerminalArgs();
        args.Tag = tag;
        Assert.Throws<BridgeErrorException>(() => Plan(args));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalPartCannotAlsoRenameOrChooseBetweenDuplicateTerminals(bool duplicateIsMain)
    {
        DeviceUpdateArgs args = TerminalArgs();
        args.NewTag = "+A1-XD6";
        Assert.Throws<BridgeErrorException>(() => Plan(args));
        args.NewTag = "";
        _store.Targets.Add(new DevicePartTarget { Id = "2", Name = args.Tag, Tag = args.Tag, IsMain = duplicateIsMain, Category = "Terminal" });
        Assert.Contains("found 2", Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("part")]
    [InlineData("identity")]
    [InlineData("name")]
    public void TerminalPartReadbackMismatchRestoresOriginalReferences(string mismatch)
    {
        DeviceUpdateArgs args = TerminalArgs();
        args.Fingerprint = Plan(args).Fingerprint;
        _store.Mismatch = mismatch;
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Execution);
        Assert.Empty(_store.Targets[0].Parts);
    }

    private DeviceUpdateArgs TerminalArgs()
    {
        DevicePartTarget target = _store.Targets[0];
        target.Name = target.Tag = "+A1-XD5:1";
        target.IsMain = false;
        target.Category = "Terminal";
        _store.Database.Parts.Add(new PartSearchItem { PartNumber = "PXC.3211813", Variant = "1" });
        return new() { Project = "Line1", Tag = target.Name, Part = "PXC.3211813" };
    }

    private static DeviceUpdateArgs Args() => new() { Project = "Line1", Tag = Tag, Part = Part };
    private WritePlan Plan(DeviceUpdateArgs args) => (WritePlan)new DeviceUpdatePlanOperation(_catalog, _reader, _renamer, _store, _store).Execute(args)!;
    private DeviceUpdateData Execute(DeviceUpdateArgs args) => (DeviceUpdateData)new DeviceUpdateExecuteOperation(_catalog, _reader, _renamer, _transactions, _store, _store).Execute(args)!;

    private sealed class Store(FakeDeviceRenamer renamer, FakeTransactions transactions) : IDevicePartAssigner, IPartsDatabase
    {
        public PartsDatabaseContent Database { get; } = new() { Parts = [new PartSearchItem { PartNumber = Part, Variant = "1" }] };
        public List<DevicePartTarget> Targets { get; } = [];
        public List<(string Id, string Part, string Variant)> Adds { get; } = [];
        public int Reads { get; private set; }
        public bool ReadAfterCommit { get; private set; }
        public string? Mismatch { get; set; }
        public bool KeepPartAfterRollback { get; set; }
        public bool KeepNameAfterRollback { get; set; }
        public PartsDatabaseContent Read(string? language) => Database;
        public IReadOnlyList<DevicePartTarget> ReadMainFunctions(string projectPath, string tag, bool includeTerminals = false)
        {
            Reads++;
            bool committed = transactions.Calls.Contains("Commit") && !transactions.Calls.Contains("Rollback");
            ReadAfterCommit |= committed;
            if (committed && Mismatch == "missing")
            { return []; }
            return [.. Targets.Select(m => new DevicePartTarget
            {
                IsMain = m.IsMain, Category = m.Category,
                Id = committed && Mismatch == "identity" ? "wrong" : m.Id,
                Name = committed && Mismatch == "name" ? "wrong" : renamer.Renames.Count > 0 && !transactions.Calls.Contains("Rollback") ? renamer.Renames[^1].NewTag : m.Name,
                Tag = renamer.Renames.Count > 0 && !transactions.Calls.Contains("Rollback") ? renamer.Renames[^1].NewTag : m.Tag,
                VisibleName = m.VisibleName, Page = m.Page,
                Parts = committed && Mismatch == "part" ? [] : [.. m.Parts.Select(p => new DevicePart { PartNumber = p.PartNumber, Variant = p.Variant, Count = p.Count })],
            })];
        }
        public void AddPart(string projectPath, string mainId, string part, string variant)
        {
            Assert.DoesNotContain("Commit", transactions.Calls);
            Adds.Add((mainId, part, variant));
            Targets.Single(m => m.Id == mainId).Parts.Add(new DevicePart { PartNumber = part, Variant = variant, Count = 1 });
        }
    }
}
