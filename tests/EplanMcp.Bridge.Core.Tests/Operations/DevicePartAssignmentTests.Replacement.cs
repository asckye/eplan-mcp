using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DevicePartAssignmentTests
{
    private static DevicePart Reference(string number, string variant = "1", int count = 1) =>
        new() { PartNumber = number, Variant = variant, Count = count };

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, 2)]
    public void ReplacementKeepsPositionAndQuantities(bool following, int? count)
    {
        var references = _store.Targets[0].Parts;
        references.Add(Reference("OLD", "A", 4));
        if (following)
        { references.AddRange([Reference("SECOND", "B", 7), Reference("THIRD", "C", 8)]); }
        var args = Args();
        args.ReplacePart = " old ";
        args.Count = count;
        WritePlan plan = Plan(args);
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("OLD / A ×4", $"{Part} / 1 ×{count ?? 4}"), (change.OldValue, change.NewValue));
        Assert.Equal(following, plan.Details!.ContainsKey("warning"));
        args.Fingerprint = plan.Fingerprint;
        DeviceUpdateData result = Execute(args);
        Assert.Equal(("OLD", "A", 4), (result.ReplacedPart!.PartNumber, result.ReplacedPart.Variant, result.ReplacedPart.Count));
        Assert.Equal((Part, "1", count ?? 4), (references[0].PartNumber, references[0].Variant, references[0].Count));
        if (following)
        {
            Assert.Equal([("SECOND", "B", 7), ("THIRD", "C", 8)], references.Skip(1).Select(p => (p.PartNumber, p.Variant, p.Count)));
        }
        Assert.Empty(_store.Transfers);
        Assert.Equal(0, _store.PartScopes);
        Assert.True(_store.ReadAfterCommit);
    }

    [Fact]
    public void ReplacementChangesVariantOnExactTerminal()
    {
        var terminal = _store.Targets[0];
        terminal.Category = "Terminal";
        terminal.IsMain = false;
        terminal.Name = terminal.Tag = Tag + ":2";
        terminal.Parts.Add(Reference(Part, "2", 3));
        var args = Args();
        args.Tag = terminal.Name;
        args.ReplacePart = Part;
        args.Fingerprint = Plan(args).Fingerprint;
        Assert.Equal("2", Execute(args).ReplacedPart!.Variant);
        Assert.Equal(("1", 3), (Assert.Single(terminal.Parts).Variant, terminal.Parts[0].Count));
    }

    [Theory]
    [InlineData("noPart")]
    [InlineData("transfer")]
    [InlineData("blank")]
    [InlineData("control")]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("caseAmbiguous")]
    [InlineData("invalidInheritedCount")]
    [InlineData("sameVariant")]
    [InlineData("otherDuplicate")]
    public void InvalidReplacementStartsNothing(string scenario)
    {
        var args = Args();
        args.ReplacePart = "OLD";
        var references = _store.Targets[0].Parts;
        references.Add(Reference("OLD", "2"));
        switch (scenario)
        {
            case "noPart":
                args.Part = null;
                break;
            case "transfer":
                args.UpdateFromParts = true;
                break;
            case "blank":
                args.ReplacePart = " ";
                break;
            case "control":
                args.ReplacePart = "OLD\t";
                break;
            case "missing":
                args.ReplacePart = "ABSENT";
                break;
            case "ambiguous":
                references.Add(Reference("OLD"));
                break;
            case "caseAmbiguous":
                references.Add(Reference("Old"));
                args.ReplacePart = "old";
                break;
            case "invalidInheritedCount":
                references[0].Count = 0;
                break;
            case "sameVariant":
                args.ReplacePart = Part;
                references.Add(Reference(Part));
                break;
            case "otherDuplicate":
                references.Add(Reference(Part));
                break;
        }
        args.Fingerprint = "unused";
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(args));
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (ex.Error.Reason, ex.Error.Execution));
        if (scenario is "missing" or "ambiguous" or "caseAmbiguous")
        { Assert.Contains("OLD / 2", ex.Error.Metadata!["violations"], StringComparison.Ordinal); }
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void ExactCaseWinsAndChangingReplacePartInvalidatesPlan()
    {
        _store.Targets[0].Parts.AddRange([Reference("OLD"), Reference("old")]);
        var args = Args();
        args.ReplacePart = "OLD";
        args.Fingerprint = Plan(args).Fingerprint;
        args.ReplacePart = "old";
        Assert.NotEqual(args.Fingerprint, Plan(args).Fingerprint);
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void ReorderedTailOnReadbackRollsBack()
    {
        _store.Targets[0].Parts.AddRange([Reference("OLD"), Reference("B"), Reference("C")]);
        var args = Args();
        args.ReplacePart = "OLD";
        args.Fingerprint = Plan(args).Fingerprint;
        _store.Mismatch = "order";
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Execution);
        Assert.Equal(["OLD", "B", "C"], _store.Targets[0].Parts.Select(p => p.PartNumber));
    }
}
