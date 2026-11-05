extern alias host;
using System.Runtime.Versioning;
using System.Text.Json;
using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;
using host::EplanMcp.Host.Bridge;
using host::EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests.Reporting;

[Trait("Size", "Small")]
public sealed class CallableCoverageTests
{
    [Fact]
    public void RefusalsKeepTheDenominatorAndCountGroupsWhileAllowingTheOtherSide()
    {
        string[] names = ["Function.UpdateLocalTemplate", "MacroBox.RepresentationType", "Page.ConvertToGraphics"];
        Unit[] units = [.. names.Select(n => new Unit("O:Eplan.EplApi.DataModel." + n, ApiCatalog.Operation, "N", "T:N.T", n.Contains("MacroBox", StringComparison.Ordinal) ? "property" : "method", null))];
        var status = units.ToDictionary(u => u.Key, _ => "registered", StringComparer.Ordinal);
        ApiRule[] rules = [.. units.Select(u => new ApiRule { Match = u.Key, Status = "refused", Sides = ["write"], Reason = "unsafe", Note = "crash", Source = "api-notes A77" })];
        string trials = "group\tsignature\tstatus\treason\n" + string.Join('\n', units.Where(u => u.Kind == "method").Select(u => u.Key + "\tsignature\twrite\t-"));
        CallableCoverage coverage = CallableCoverage.Build(units, status, trials, [units[1].Key], [], rules);
        Assert.Equal((3, 0, 1, 1, 3), (coverage.Operation.Denominator, coverage.Operation.Tools, coverage.Operation.ApiTry, coverage.Operation.Callable, coverage.Operation.RefusedUnsafe));
        Assert.Equal(3, Assert.Single(coverage.OperationsByNamespace).RefusedUnsafe);
        using JsonDocument json = JsonDocument.Parse(CoverageJson.Serialize(coverage));
        Assert.Equal(3, json.RootElement.GetProperty("operation").GetProperty("refused_unsafe").GetInt32());
        Assert.All(status.Values, s => Assert.Equal("registered", s));
        rules[1].Sides = ["read"];
        Assert.Contains(units[1].Key, CallableCoverage.ApiTryGroups(trials, [units[1].Key], rules, [units[1].Key]));
        rules[1].Sides = ["read", "write"];
        Assert.Empty(CallableCoverage.ApiTryGroups(trials, [units[1].Key], rules, [units[1].Key]));
        rules[0].Sides = ["call"];
        Assert.Empty(CallableCoverage.ApiTryGroups(trials, [], rules));
    }

    private const string Trials = "group\tsignature\tstatus\treason\n"
        + "O:N.T.Shared\tM:N.T.Shared()\tread\t-\n"
        + "O:N.T.Shared\tM:N.T.Shared(System.Int32)\twrite\t-\n"
        + "O:N.T.Read\tM:N.T.Read()\tread\t-\n"
        + "O:N.T.Write\tM:N.T.Write()\twrite\t-\n"
        + "O:N.T.Blocked\tM:N.T.Blocked()\texcluded\tunsupported\n"
        + "O:N.T.Old\tM:N.T.Old()\tread\t-\n"
        + "O:N.T.Gui\tM:N.T.Gui()\twrite\t-\n"
        + "O:Helper.Value\tM:Helper.Value()\tread\t-\n";

    [Fact]
    public void Build_RestrictsBothPathsToTheSameDenominatorAfterAllExclusions()
    {
        CallableCoverage coverage = FakeCatalog();

        Assert.Equal((6, 2, 4, 5), (coverage.Operation.Denominator, coverage.Operation.Tools, coverage.Operation.ApiTry, coverage.Operation.Callable));
        Assert.Equal(1, coverage.Operation.OutsideDenominator["excluded:deprecated"]);
        Assert.Equal(1, coverage.Operation.OutsideDenominator["excluded:overlay:gui"]);
    }

    [Fact]
    public void Build_HelperGroupOutsideTheOperationLayerIsReportedButNeverAdded()
    {
        CallableCoverage coverage = FakeCatalog();

        Assert.Equal(1, coverage.Operation.OutsideDenominator["not_in_operation_layer"]);
        Assert.Equal(3, coverage.Operation.OutsideDenominator.Values.Sum());
        Assert.Equal(5, coverage.Operation.Callable);
    }

    [Fact]
    public void Build_UnionDeduplicatesReadWriteOverloadsAndPropertiesAcrossBothPaths()
    {
        CallableCoverage coverage = FakeCatalog();

        Assert.Equal(5, coverage.Operation.Callable); // tools-only, shared, read, write, property; blocked remains unreachable.
        Assert.Equal([("N", 4), ("N.Child", 1)], coverage.OperationsByNamespace.Select(n => (n.Scope, n.Callable)));
        Assert.Equal(coverage.Operation.Callable, coverage.OperationsByNamespace.Sum(n => n.Callable));
    }

    [Fact]
    public void Json_AddsAnIndependentSummaryWithoutChangingUnitStatuses()
    {
        using JsonDocument json = JsonDocument.Parse(CoverageJson.Serialize(FakeCatalog()));
        JsonElement operation = json.RootElement.GetProperty("operation");

        Assert.Equal(4, operation.GetProperty("apiTry").GetInt32());
        Assert.Equal(5, operation.GetProperty("callable").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("units", out _));
    }

    [Fact]
    public void Build_ActionsApplyCatalogAndOverlayExclusionsDenyRulesAndCaseInsensitiveWhitelist()
    {
        Unit[] actions = [Action("Allowed"), Action("AllowedTyped", "MODE"), Action("Tool"), Action("Old", exclusion: "interactive_ui"),
            Action("Gui"), Action("ExportData"), Action("Invalid-Type", "bad-value"), Action("CaseDuplicate"), Action("caseduplicate", exclusion: "interactive_ui")];
        Dictionary<string, string> status = actions.ToDictionary(a => a.Key, a => a.Exclusion is null ? "registered" : "excluded:" + a.Exclusion, StringComparer.Ordinal);
        status["A:Tool"] = "implemented";
        status["A:Gui"] = "excluded:overlay:gui";

        CallableCoverage coverage = CallableCoverage.Build(actions, status, "", [], ["A:tool"]);

        Assert.Equal(new ActionReachability(6, 1, 2), coverage.Action);
    }

    [Fact]
    public void Build_EmptyCatalogAndTrialInputsYieldZeroCounts()
    {
        CallableCoverage coverage = CallableCoverage.Build([], new Dictionary<string, string>(), "", [], []);

        Assert.Equal((0, 0, 0, 0), (coverage.Operation.Denominator, coverage.Operation.Tools, coverage.Operation.ApiTry, coverage.Operation.Callable));
        Assert.Empty(coverage.Operation.OutsideDenominator);
        Assert.Empty(coverage.OperationsByNamespace);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Build_ActionTrialCountMatchesHostCheckForEveryShippedCatalogRow()
    {
        ActionCatalogItem[] rows = ActionCatalogReader.Official();
        var catalog = new ActionCatalogData(rows, null, []);
        Unit[] units = [.. rows.Select(row => Action(row.Name, row.Type, row.ExcludedReason))];
        Dictionary<string, string> status = units.ToDictionary(u => u.Key,
            u => u.Exclusion is null ? "registered" : "excluded:" + u.Exclusion, StringComparer.Ordinal);
        int expected = rows.Count(row =>
        {
            try
            {
                ActionTryTools.Check(catalog, "Scratch", row.Name, row.Type, default);
                return true;
            }
            catch (BridgeCallException)
            {
                return false;
            }
        });

        CallableCoverage coverage = CallableCoverage.Build(units, status, "", [], CallableCoverage.ActionWhitelist());

        Assert.True(expected > 0);
        Assert.Equal(expected, coverage.Action.ActionTry);
    }

    private static CallableCoverage FakeCatalog()
    {
        Unit Operation(string name, string scope = "N", string? exclusion = null) => new("O:N.T." + name, ApiCatalog.Operation, scope, "T:N.T", name == "Property" ? "property" : "method", exclusion);
        Unit[] units = [Operation("ToolsOnly"), Operation("Shared"), Operation("Read"), Operation("Write", "N.Child"),
            Operation("Property"), Operation("Blocked"), Operation("Old", exclusion: "deprecated"), Operation("Gui")];
        var status = units.ToDictionary(u => u.Key, u => "registered", StringComparer.Ordinal);
        status["O:N.T.ToolsOnly"] = "implemented";
        status["O:N.T.Shared"] = "live_verified";
        status["O:N.T.Old"] = "excluded:deprecated";
        status["O:N.T.Gui"] = "excluded:overlay:gui";
        return CallableCoverage.Build(units, status, Trials, ["O:N.T.Property", "O:N.T.Shared", "O:Helper.Value"], []);
    }

    private static Unit Action(string name, string? type = null, string? exclusion = null) =>
        new("A:" + name + (type is null ? "" : "/" + type), ApiCatalog.Action, name, null, type, exclusion);
}
