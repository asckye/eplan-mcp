using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>project.update.plan and project.update.execute against a fake project (ADR-0052).</summary>
[Trait("Size", "Small")]
public sealed class ProjectUpdateOperationTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true });
    private readonly Properties _properties = new();
    private readonly FakeTransactions _transactions = new();

    [Fact]
    public void Plan_ListsOnlyWhatChanges_AndWritesNothing()
    {
        WritePlan plan = Plan(Args((10015, "ACME"), (10016, "Old street")));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "project", "10015", "Old name", "ACME"), (change.Action, change.Kind, change.Property, change.OldValue, change.NewValue));
        Assert.Empty(_properties.Written);
    }

    [Fact]
    public void Execute_WritesReadsBackAndCommitsOnce()
    {
        ProjectUpdateArgs args = Args((10015, "ACME"), (10011, "zh_CN@输送线;en_US@Line;"));
        args.Fingerprint = Plan(args).Fingerprint;
        ProjectUpdateData data = Execute(args);
        Assert.Equal(["10015", "10011"], data.Properties.Select(p => p.Property));
        Assert.Equal(["10015", "10011"], _properties.Written);
        Assert.Equal(1, _transactions.Calls.Count(c => c == "Commit"));
    }

    [Fact]
    public void Execute_LanguageOrderDoesNotCount()
    {
        _properties.Stored["10011"] = "en_US@Line;zh_CN@输送线;";
        ProjectUpdateArgs args = Args((10011, "zh_CN@输送线;en_US@Line;"));
        WritePlan plan = Plan(args);
        Assert.Empty(plan.Changes);
        args.Fingerprint = plan.Fingerprint;
        Assert.Empty(Execute(args).Properties);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_DifferentReadBack_RollsBack()
    {
        ProjectUpdateArgs args = Args((10015, "ACME"));
        args.Fingerprint = Plan(args).Fingerprint;
        _properties.Stores = _ => "Something else";
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));
        Assert.Equal(ErrorReason.EplanOperationFailed, error.Error.Reason);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_ChangedSinceThePlan_IsStale()
    {
        ProjectUpdateArgs args = Args((10015, "ACME"));
        args.Fingerprint = Plan(args).Fingerprint;
        _properties.Stored["10015"] = "Changed by the engineer";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_properties.Written);
    }

    [Theory]
    [InlineData(10002)]
    [InlineData(10050)]
    [InlineData(10000)]
    public void Plan_PropertyOutsideTheList_IsDenied(int id) =>
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Plan(Args((id, "x")))).Error.Reason);

    [Fact]
    public void Plan_TemplateProperty_GoesByName_OnlyWithItsPrefix()
    {
        var args = new ProjectUpdateArgs { Project = "Line1", Properties = [new() { Name = "ESS.Project.Control_Voltage", Value = "24 V DC" }] };
        Assert.Equal("ESS.Project.Control_Voltage", Assert.Single(Plan(args).Changes).Property);
        args.Properties[0].Name = "EPLAN.Project.Other";
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void Plan_BadArguments_AreInvalid()
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(Args((10015, "a"), (10015, "b")))).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(Args((10015, "zh_CN@公司;")))).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(Args((10015, new string('x', 501))))).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(Args((10015, "a\nb")))).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(new ProjectUpdateArgs { Project = "Line1", Properties = [] })).Error.Reason);
    }

    [Fact]
    public void Plan_Translate_FillsOnlyMultilingualValuesInOneLanguage()
    {
        WritePlan plan = Plan(new ProjectUpdateArgs { Project = "Line1", Translate = true, Properties = [new() { Id = 10011, Value = "Line" }, new() { Id = 10015, Value = "ACME" }] });
        Assert.Equal(["zh_CN@Line;en_US@Line;", "ACME"], plan.Changes.Select(c => c.NewValue));
    }

    [Fact]
    public void Plan_AllListedAndTemplateProperties_FitOneCall()
    {
        var args = new ProjectUpdateArgs { Project = "Line1", Properties = [.. ProjectUpdateArgs.TitleBlock.Keys.Select(id => new ProjectPropertyValue { Id = id, Value = "x" }), new() { Name = "ESS.Project.Control_Voltage", Value = "24 V DC" }] };
        Assert.Equal(ProjectUpdateArgs.TitleBlock.Count + 1, Plan(args).Changes.Count);
    }

    private static ProjectUpdateArgs Args(params (int Id, string Value)[] values) =>
        new() { Project = "Line1", Properties = [.. values.Select(v => new ProjectPropertyValue { Id = v.Id, Value = v.Value })] };

    private WritePlan Plan(ProjectUpdateArgs args) => (WritePlan)new ProjectUpdatePlanOperation(_catalog, _properties, new FakeTranslator()).Execute(args)!;

    private ProjectUpdateData Execute(ProjectUpdateArgs args) => (ProjectUpdateData)new ProjectUpdateExecuteOperation(_catalog, _properties, new FakeTranslator(), _transactions).Execute(args)!;

    private sealed class Properties : IProjectProperties
    {
        public Dictionary<string, string> Stored { get; } = new() { ["10015"] = "Old name", ["10016"] = "Old street", ["10011"] = "" };
        public List<string> Written { get; } = [];
        public Func<string, string> Stores { get; set; } = value => value;
        public string Read(string projectPath, string key) => Stored.GetValueOrDefault(key, string.Empty);
        public void Write(string projectPath, string key, string value)
        {
            Written.Add(key);
            Stored[key] = Stores(value);
        }
    }
}
