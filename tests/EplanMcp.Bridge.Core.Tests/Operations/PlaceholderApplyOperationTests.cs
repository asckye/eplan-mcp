using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// placeholder.apply.plan and .execute against fakes (docs/tools/eplan_placeholder_apply.md, sections 2, 4 and 5): the
/// choice of the placeholder, the plan and its fingerprint, PLAN_STALE, the transaction, and the read-back that rolls
/// back when the placeholder itself changed.
/// </summary>
[Trait("Size", "Small")]
public sealed class PlaceholderApplyOperationTests
{
    private const string Tm1 = "=TM1+A1&EFS1/1";

    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };

    private readonly FakePlaceholderReader _reader = new();
    private readonly FakePageReader _pages = new();
    private readonly FakeTransactions _transactions = new();
    private readonly FakePlaceholderWriter _writer;

    public PlaceholderApplyOperationTests()
    {
        _pages.Pages.Add(new PageSummary { Name = Tm1, Type = "Circuit" });
        _reader.Placeholders.AddRange([FakePlaceholderReader.Sample(Tm1), FakePlaceholderReader.Sample("=TM2+A1&EFS1/1")]);
        _writer = new FakePlaceholderWriter(_reader);
    }

    [Fact]
    public void Plan_NamesTheValueSet_AndGivesItsValuesAndTheCurrentValuesInTheDetails()
    {
        WritePlan plan = Plan(Args("mA"));

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "placeholder", Tm1 + " (no name)", "value set", null, "mA"), (change.Action, change.Kind, change.Name, change.Property, change.OldValue, change.NewValue));
        Assert.Equal(("Line1", _line1.Path), (plan.Project, plan.ProjectPath));
        Assert.Equal("T1\t4-20 mA\nT2\t20 mA", plan.Details!["values"]);
        Assert.Equal("0-10 V\n10 V", plan.Details["currentValues"]);
        Assert.Equal((Tm1, string.Empty, "2"), (plan.Details["page"], plan.Details["placeholder"], plan.Details["assignedObjects"]));
        Assert.Equal(64, plan.Fingerprint.Length);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Plan_TheFingerprint_FollowsTheValueSetAndTheCurrentValues()
    {
        string mA = Plan(Args("mA")).Fingerprint;
        string v = Plan(Args("V")).Fingerprint;
        _reader.Placeholders[0].CurrentValues[0].Raw = "??_??@changed;";

        Assert.NotEqual(mA, v);
        Assert.NotEqual(mA, Plan(Args("mA")).Fingerprint);
    }

    [Theory]
    [InlineData("X", null, ErrorReason.InvalidArgument, "(no name): V, mA")] // no such value set
    [InlineData("v", null, ErrorReason.InvalidArgument, "(no name): V, mA")] // case-sensitive
    [InlineData("mA", "nope", ErrorReason.InvalidArgument, "(no name): V, mA")] // no such name on the page
    public void Plan_NoPlaceholderFits_IsInvalidArgument_WithThePagesPlaceholders(string valueSet, string? name, string reason, string candidates)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(valueSet, name))).Error;

        Assert.Equal(reason, error.Reason);
        Assert.Equal(candidates, error.Metadata!["candidates"]);
    }

    [Fact]
    public void Plan_TwoPlaceholdersFit_IsAmbiguous_AndANameChoosesOne()
    {
        _reader.Placeholders.Add(FakePlaceholderReader.Sample(Tm1, "Motor"));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args("mA"))).Error;

        Assert.Equal(ErrorReason.AmbiguousName, error.Reason);
        Assert.Equal("(no name): V, mA\n'Motor': V, mA", error.Metadata!["candidates"]);
        Assert.Equal(Tm1 + " Motor", Plan(Args("mA", "Motor")).Changes[0].Name);
    }

    [Fact]
    public void Plan_MissingArguments_AreReportedTogether_AndAnUnknownPageIsNotFound()
    {
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Plan(new PlaceholderApplyArgs())).Error;
        BridgeError page = Assert.Throws<BridgeErrorException>(() => Plan(new PlaceholderApplyArgs { Project = "Line1", Page = "=X/1", ValueSet = "V" })).Error;

        Assert.All(["'project'", "'page'", "'value_set'"], v => Assert.Contains(v, missing.Metadata!["violations"], StringComparison.Ordinal));
        Assert.Equal(ErrorReason.PageNotFound, page.Reason);
    }

    [Fact]
    public void Execute_AppliesInOneTransaction_AndReportsTheCurrentValuesBeforeAndAfter()
    {
        PlaceholderApplyArgs args = Args("mA");
        args.Fingerprint = Plan(args).Fingerprint;

        PlaceholderApplyData data = Execute(args);

        Assert.Equal([(Tm1, "", "mA")], _writer.Applied);
        Assert.Equal(["Begin AI: eplan_placeholder_apply", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal(("Line1", Tm1, "", "mA", true), (data.Project, data.Page, data.Placeholder, data.ValueSet, data.CurrentValuesChanged));
        Assert.Equal([("T1", "4-20 mA"), ("T2", "20 mA")], data.Values.Select(v => (v.Variable, v.Text)));
        Assert.Equal(["0-10 V", "10 V"], data.CurrentValuesBefore.Select(c => c.Text));
        Assert.Equal(["4-20 mA", "20 mA"], data.CurrentValuesAfter.Select(c => c.Text));
    }

    [Fact]
    public void Execute_TheValueSetAlreadyApplied_CommitsAndSaysTheCurrentValuesDidNotChange()
    {
        PlaceholderApplyArgs args = Args("V");
        args.Fingerprint = Plan(args).Fingerprint;

        Assert.False(Execute(args).CurrentValuesChanged);
        Assert.Contains("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutTheFingerprint_OrAfterAChange_IsNotStarted()
    {
        PlaceholderApplyArgs args = Args("mA");
        args.Fingerprint = Plan(args).Fingerprint;
        _reader.Placeholders[0].CurrentValues[1].Raw = "??_??@edited;";

        BridgeError stale = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        BridgeError none = Assert.Throws<BridgeErrorException>(() => Execute(Args("mA"))).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (stale.Reason, stale.Execution));
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (none.Reason, none.Execution));
        Assert.Empty(_transactions.Calls);
        Assert.Empty(_writer.Applied);
    }

    [Fact]
    public void Execute_ThePlaceholderChangedByTheApply_RollsBack()
    {
        PlaceholderApplyArgs args = Args("mA");
        args.Fingerprint = Plan(args).Fingerprint;
        _writer.LoseValueSet = true;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack, "its value sets changed"), (error.Reason, error.Execution, error.Metadata!["problem"]));
        Assert.Equal(["Begin AI: eplan_placeholder_apply", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    private static PlaceholderApplyArgs Args(string valueSet, string? name = null) =>
        new() { Project = "Line1", Page = Tm1, Placeholder = name, ValueSet = valueSet };

    private WritePlan Plan(PlaceholderApplyArgs args) =>
        (WritePlan)new PlaceholderApplyPlanOperation(new FakeCatalog(_line1), _pages, _reader).Execute(args)!;

    private PlaceholderApplyData Execute(PlaceholderApplyArgs args) =>
        (PlaceholderApplyData)new PlaceholderApplyExecuteOperation(new FakeCatalog(_line1), _pages, _reader, _writer, _transactions).Execute(args)!;

    /// <summary>
    /// Applies a value set as S29 saw EPLAN do (api-notes A33): the current values become the value set's values. With
    /// <see cref="LoseValueSet"/>, the placeholder read back has lost a value set, a read-back the operation refuses.
    /// </summary>
    private sealed class FakePlaceholderWriter(FakePlaceholderReader reader) : IPlaceholderWriter
    {
        public List<(string Page, string Name, string ValueSet)> Applied { get; } = [];

        public bool LoseValueSet { get; set; }

        public PlaceholderRecord Apply(string projectPath, string page, string name, string valueSet)
        {
            Applied.Add((page, name, valueSet));
            PlaceholderRecord placeholder = Assert.Single(reader.Placeholders, p => p.Page == page && p.Name == name);
            var after = new PlaceholderRecord
            {
                Properties = placeholder.Properties,
                Page = placeholder.Page,
                ValueSets = LoseValueSet ? [.. placeholder.ValueSets.Skip(1)] : placeholder.ValueSets,
                AssignedObjects = placeholder.AssignedObjects,
                Values = placeholder.Values,
                CurrentValues = [.. placeholder.Values.Where(v => v.ValueSet == valueSet).Select(v => new PlaceholderText { Text = v.Value.Text, Raw = v.Value.Raw })],
            };
            return after;
        }
    }
}
