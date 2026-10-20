using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// connection.generate.plan and connection.generate.execute against fake pages (docs/tools/eplan_connection_generate.md,
/// sections 2 to 5 and 7). Page /60 holds a macro whose 9 drawn connections are not connections yet (S9, S10); page /1
/// has its connections complete.
/// </summary>
[Trait("Size", "Small")]
public sealed class ConnectionGenerateOperationTests
{
    private const string NewPage = "=GAA+A1&EFS1/60";
    private const string FullPage = "=GAA+A1&EFS1/1";

    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _reader = new();
    private readonly FakeConnectionGenerator _generator = new();
    private readonly FakeTransactions _transactions = new();

    public ConnectionGenerateOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _reader.Pages.AddRange([new PageSummary { Name = FullPage }, new PageSummary { Name = "=GAA+A1&EFS1/2" }, new PageSummary { Name = NewPage }]);
        _generator.Connections[FullPage] = [Wire("=GAA+A1-FC1", "2", "=GAA+A1-QA1", "1"), Wire("=GAA+A1-QA1", "2", "=GAA+A1-MA1", "U1")];
        _generator.Connections[NewPage] = [];
        _generator.Objects[FullPage] = ["1/1/1/0", "1/1/2/0", "1/1/3/0"];
        _generator.Objects[NewPage] = ["1/2/1/0", "1/2/2/0"];
        _generator.Drawn[NewPage] = [.. Enumerable.Range(1, 9).Select(i => Wire("+A1-XG9:?" + i, null, "=GAA+A1-?QA1", "1" + i))];
        _transactions.Snapshot = () =>
        {
            var connections = _generator.Connections.ToDictionary(p => p.Key, p => p.Value.ToList());
            var drawn = _generator.Drawn.ToDictionary(p => p.Key, p => p.Value.ToList());
            return () =>
            {
                Restore(_generator.Connections, connections);
                Restore(_generator.Drawn, drawn);
            };
        };
    }

    [Fact]
    public void Plan_IsOneConnectionsUpdatePerPageWithTheCountNow()
    {
        WritePlan plan = Plan(Args("=gaa+a1&efs1/60", FullPage));

        Assert.Equal(
            [("update", "page", NewPage, "connections", "0", null), ("update", "page", FullPage, "connections", "2", null)],
            plan.Changes.Select(c => (c.Action, c.Kind, c.Name, c.Property, c.OldValue, c.NewValue)));
        Assert.Equal((_line1.Name, _line1.Path), (plan.Project, plan.ProjectPath));
        Assert.Empty(_generator.Generates);
    }

    [Fact]
    public void Plan_ListsEveryMissingPage()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args("=GAA+A1&EFS1/9", FullPage, "=X/1"))).Error;

        Assert.Equal((ErrorReason.PageNotFound, "=GAA+A1&EFS1/9\n=X/1"), (error.Reason, error.Metadata!["missing"]));
    }

    [Fact]
    public void Plan_TwoPagesDifferingOnlyInCase_IsAmbiguous()
    {
        _reader.Pages.Add(new PageSummary { Name = "=gaa+a1&efs1/2" });

        Assert.Equal(ErrorReason.AmbiguousName, Assert.Throws<BridgeErrorException>(() => Plan(Args("=Gaa+A1&EFS1/2"))).Error.Reason);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        var args = new ConnectionGenerateArgs { Pages = [FullPage, "=gaa+a1&efs1/1", string.Empty] };

        string[] violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"].Split('\n');

        Assert.Equal(3, violations.Length);
        Assert.Contains("'project' is required", violations[0], StringComparison.Ordinal);
        Assert.Contains("pages[1] repeats pages[0]", violations[1], StringComparison.Ordinal);
        Assert.Contains("pages[2] is required", violations[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Plan_NoPagesOrMoreThan50_IsInvalid(int count)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args([.. Enumerable.Range(1, count).Select(i => "=GAA+A1&EFS1/" + i)]))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'pages' must hold 1 to 50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GeneratesInOneTransactionAndListsTheNewConnections()
    {
        ConnectionGenerateArgs args = Args(NewPage, FullPage);
        args.Fingerprint = Plan(args).Fingerprint;

        ConnectionGenerateData data = Execute(args);

        Assert.Equal(("Line1", null), (data.Project, data.Warning));
        Assert.Equal([(NewPage, 0, 9, 9, 0), (FullPage, 2, 2, 0, 0)], data.Pages.Select(p => (p.Page, p.Before, p.After, p.AddedCount, p.RemovedCount)));
        ChangedConnection first = data.Pages[0].Added[0];
        Assert.Equal(("+A1-XG9:?1", null, "=GAA+A1-?QA1", "11"), (first.From!.Device, first.From.Pin, first.To!.Device, first.To.Pin));
        Assert.Equal([NewPage, FullPage], Assert.Single(_generator.Generates));
        Assert.Equal(["Begin AI: eplan_connection_generate", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_AgainOnTheSamePages_AddsAndRemovesNothing()
    {
        ConnectionGenerateArgs args = Args(NewPage);
        args.Fingerprint = Plan(args).Fingerprint;
        Execute(args);
        args.Fingerprint = Plan(args).Fingerprint;

        ConnectionGeneratePage page = Assert.Single(Execute(args).Pages);

        Assert.Equal((9, 9, 0, 0), (page.Before, page.After, page.AddedCount, page.RemovedCount));
        Assert.Empty(page.Added);
        Assert.Empty(page.Removed);
    }

    [Fact]
    public void Execute_ListsRemovedConnections_AndKeepsOnesWhoseEndsSwapped()
    {
        List<ConnectionRecord> full = _generator.Connections[FullPage];
        _generator.Stale[FullPage] = [.. full];
        _generator.Drawn[FullPage] = [Wire("=GAA+A1-MA1", "U1", "=GAA+A1-QA1", "2")];
        ConnectionGenerateArgs args = Args(FullPage);
        args.Fingerprint = Plan(args).Fingerprint;

        ConnectionGeneratePage page = Assert.Single(Execute(args).Pages);

        Assert.Equal((2, 1, 0, 1), (page.Before, page.After, page.AddedCount, page.RemovedCount));
        ChangedConnection removed = Assert.Single(page.Removed);
        Assert.Equal(("=GAA+A1-FC1", "2", "=GAA+A1-QA1", "1"), (removed.From!.Device, removed.From.Pin, removed.To!.Device, removed.To.Pin));
    }

    [Fact]
    public void Execute_ListsAtMost100Connections_TheRemovedFirst()
    {
        _generator.Drawn[NewPage] = [.. Enumerable.Range(1, 120).Select(i => Wire("=GAA+A1-KF" + i, "A1", "=GAA+A1-KF" + i, "A2"))];
        _generator.Stale[FullPage] = [.. _generator.Connections[FullPage]];
        ConnectionGenerateArgs args = Args(NewPage, FullPage);
        args.Fingerprint = Plan(args).Fingerprint;

        ConnectionGenerateData data = Execute(args);

        Assert.Equal([(120, 98, 0, 0), (0, 0, 2, 2)], data.Pages.Select(p => (p.AddedCount, p.Added.Count, p.RemovedCount, p.Removed.Count)));
    }

    [Fact]
    public void Execute_AfterAConnectionWasGenerated_IsPlanStaleAndGeneratesNothing()
    {
        ConnectionGenerateArgs args = Args(FullPage);
        args.Fingerprint = Plan(args).Fingerprint;
        _generator.Connections[FullPage].Add(Wire("=GAA+A1-FC2", "2", "=GAA+A1-QA2", "1")); // the engineer, meanwhile

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_generator.Generates);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_AfterAnObjectWasPlaced_IsPlanStale()
    {
        ConnectionGenerateArgs args = Args(NewPage);
        args.Fingerprint = Plan(args).Fingerprint;
        _generator.Objects[NewPage].Add("1/2/3/0");

        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
    }

    [Fact]
    public void Execute_WhenEplanFails_RollsBack()
    {
        ConnectionGenerateArgs args = Args(NewPage);
        args.Fingerprint = Plan(args).Fingerprint;
        _generator.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not generate the connections.");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Empty(_generator.Connections[NewPage]);
        Assert.Equal(["Begin AI: eplan_connection_generate", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutAFingerprint_IsNotStarted()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args(FullPage))).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Fact]
    public void Key_IsTheSameEitherWayRound_AndTreatsEmptyNamesAsNone()
    {
        Assert.Equal(ConnectionChanges.Key(Wire("-A", "1", "-B", "2")), ConnectionChanges.Key(Wire("-B", "2", "-A", "1")));
        Assert.Equal(ConnectionChanges.Key(Wire("-A", null, null, null)), ConnectionChanges.Key(Wire("-A", string.Empty, string.Empty, string.Empty)));
        Assert.NotEqual(ConnectionChanges.Key(Wire("-A", "1", "-B", "2")), ConnectionChanges.Key(Wire("-A", "2", "-B", "1")));
    }

    private static ConnectionRecord Wire(string? from, string? fromPin, string? to, string? toPin) =>
        new() { StartDevice = from, StartPin = fromPin, EndDevice = to, EndPin = toPin };

    private static void Restore<T>(Dictionary<string, List<T>> target, Dictionary<string, List<T>> saved)
    {
        target.Clear();
        foreach (KeyValuePair<string, List<T>> entry in saved)
        {
            target[entry.Key] = entry.Value;
        }
    }

    private static ConnectionGenerateArgs Args(params string[] pages) => new() { Project = "Line1", Pages = [.. pages] };

    private WritePlan Plan(ConnectionGenerateArgs args) =>
        (WritePlan)new ConnectionGeneratePlanOperation(_catalog, _reader, _generator).Execute(args)!;

    private ConnectionGenerateData Execute(ConnectionGenerateArgs args) =>
        (ConnectionGenerateData)new ConnectionGenerateExecuteOperation(_catalog, _reader, _generator, _transactions).Execute(args)!;
}
