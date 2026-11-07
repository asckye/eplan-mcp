using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>report.settings.plan and report.settings.execute against a fake project.</summary>
[Trait("Size", "Small")]
public sealed class ReportSettingsOperationTests
{
    private const string Summarize = "PartsListGui.SummarizePartsOfSameDT";
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true });
    private readonly Settings _settings = new();

    [Fact]
    public void Plan_ListsOnlyWhatChanges()
    {
        WritePlan plan = Plan(new() { [Summarize] = false, ["PartsListGui.IncludeTerminalStripParts"] = true });
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal((Summarize, "true", "false"), (change.Property, change.OldValue, change.NewValue));
        Assert.True(_settings.Values[Summarize]);
    }

    [Theory]
    [InlineData("TRANSLATEGUI.SOURCE_LANGUAGE")]
    [InlineData("PartsListGui.Other")]
    public void Plan_ASettingOffTheList_IsInvalid(string name) =>
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(new() { [name] = true })).Error.Reason);

    [Fact]
    public void Plan_NoSetting_IsInvalid() =>
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan([])).Error.Reason);

    [Fact]
    public void Execute_SetsAndReadsBack()
    {
        Execute(new() { [Summarize] = false });
        Assert.False(_settings.Values[Summarize]);
    }

    [Fact]
    public void Execute_ChangedSinceThePlan_IsStale()
    {
        var args = new ReportSettingsArgs { Project = "Line1", Settings = new() { [Summarize] = false } };
        args.Fingerprint = ((WritePlan)new ReportSettingsPlanOperation(_catalog, _settings).Execute(args)!).Fingerprint;
        _settings.Values["PartsListGui.Other"] = true;
        _settings.Values[Summarize] = false;
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => new ReportSettingsExecuteOperation(_catalog, _settings).Execute(args)).Error.Reason);
    }

    [Fact]
    public void Execute_DifferentReadBack_RestoresTheOldValues()
    {
        _settings.IgnoreFalse = true;
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(new() { [Summarize] = false }));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.True(_settings.Values[Summarize]);
    }

    private WritePlan Plan(Dictionary<string, bool> settings) =>
        (WritePlan)new ReportSettingsPlanOperation(_catalog, _settings).Execute(new ReportSettingsArgs { Project = "Line1", Settings = settings })!;

    private void Execute(Dictionary<string, bool> settings)
    {
        var args = new ReportSettingsArgs { Project = "Line1", Settings = settings };
        args.Fingerprint = ((WritePlan)new ReportSettingsPlanOperation(_catalog, _settings).Execute(args)!).Fingerprint;
        new ReportSettingsExecuteOperation(_catalog, _settings).Execute(args);
    }

    private sealed class Settings : IReportSettings
    {
        public Dictionary<string, bool> Values { get; } = new() { [Summarize] = true, ["PartsListGui.IncludeTerminalStripParts"] = true };

        public bool IgnoreFalse { get; set; }

        public bool Read(string projectPath, string setting) => Values.GetValueOrDefault(setting);

        public void Write(string projectPath, string setting, bool value)
        {
            if (value || !IgnoreFalse)
            { Values[setting] = value; }
        }
    }
}
