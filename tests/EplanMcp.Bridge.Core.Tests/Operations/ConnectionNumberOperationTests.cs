using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// connection.number.plan and connection.number.execute against a fake that numbers the way spike S21 found EPLAN
/// numbers the sample project's connections (api-notes A24: '*' → 433, none → 011): the scheme check, the overwrite
/// modes, the change list, the backup before the transaction, and the read-back by connection. Medium: the backup goes
/// into a real temporary folder.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ConnectionNumberOperationTests : IDisposable
{
    private const string Page = "=F+A2&EFS1/2";

    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-wires-").FullName;
    private readonly FakePageReader _pages = new();
    private readonly FakeConnectionNumberer _numberer = new();
    private readonly FakeProjectBackup _backup;
    private readonly FakeTransactions _transactions = new();

    public ConnectionNumberOperationTests()
    {
        _pages.Pages.Add(new PageSummary { Name = Page });
        _backup = new FakeProjectBackup(_pages);
        _numberer.Connections.AddRange(
        [
            new DesignatedConnection { Id = "w1", Designation = "*", Page = Page },
            new DesignatedConnection { Id = "w2", Page = "=EA+A1&EFS1/1" },
            new DesignatedConnection { Id = "w3", Designation = "100", Page = Page },
            new DesignatedConnection { Id = "w4", Designation = "200", Page = Page },
            new DesignatedConnection { Id = "w5" }, // a 3D connection: no page
        ]);
        _numberer.Manual.Add("w3");
        _numberer.Numbers["w1"] = "433";
        _numberer.Numbers["w2"] = "011";
        _numberer.Numbers["w3"] = "300";
        _numberer.Numbers["w4"] = "201";
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Plan_TheWholeProject_ExceptManualByDefault()
    {
        WritePlan plan = Plan(new ConnectionNumberArgs { Project = "Line1", Scheme = " Standard " });

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "connections", "Line1", "numbering", null), (change.Action, change.Kind, change.Name, change.Property, change.NewValue));
        Assert.Equal("wire numbers in the project (5 connections, 3 numbered), scheme: Standard, overwrite: except manual", change.OldValue);
        Assert.Equal(("5", "3", "Standard", "except_manual"), (plan.Details!["connections"], plan.Details["designatedBefore"], plan.Details["scheme"], plan.Details["overwrite"]));
        Assert.Empty(_numberer.Designations);
    }

    [Fact]
    public void Plan_OverwriteAll_InAnotherScheme_AndAProjectWithoutConnections()
    {
        WritePlan all = Plan(new ConnectionNumberArgs { Project = "Line1", Scheme = "Potenzialorientiert", Overwrite = "all" });
        _numberer.Connections.Clear();
        WritePlan none = Plan(new ConnectionNumberArgs { Project = "Line1", Scheme = "Standard" });

        Assert.EndsWith("scheme: Potenzialorientiert, overwrite: all", Assert.Single(all.Changes).OldValue, StringComparison.Ordinal);
        Assert.Empty(none.Changes);
        Assert.Equal("0", none.Details!["connections"]);
    }

    [Fact]
    public void Plan_ASchemeTheProjectDoesNotOffer_IsInvalidWithTheOfferedOnes()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new ConnectionNumberArgs { Project = "Line1", Scheme = "Potential" })).Error;

        Assert.Equal((ErrorReason.InvalidArgument, "Standard\nPotenzialorientiert\nVerbindungsorientiert"), (error.Reason, error.Metadata!["candidates"]));
        Assert.StartsWith("EPLAN has no wire numbering scheme 'Potential'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_NamesEveryArgumentProblemAtOnce()
    {
        BridgeError invalid = Assert.Throws<BridgeErrorException>(() => Plan(new ConnectionNumberArgs { Scheme = " ", Overwrite = "manual" })).Error;

        Assert.Equal(
            "'project' is required for writes: a name or path from eplan_project_list.\n'scheme' is required: a wire numbering scheme such as Standard.\n'overwrite' must be one of except_manual, none, all.",
            invalid.Metadata!["violations"]);
    }

    [Theory]
    [InlineData("except_manual", new[] { "w1", "w2", "w4" }, 4)] // the manual 100 stays
    [InlineData("none", new[] { "w2" }, 4)]
    [InlineData("all", new[] { "w1", "w2", "w3", "w4" }, 4)]
    public void Execute_BacksUpFirst_ThenNumbersInOneTransaction_AsTheOverwriteModeAllows(string overwrite, string[] renumbered, int designatedAfter)
    {
        var args = new ConnectionNumberArgs { Project = "Line1", Scheme = "Standard", Overwrite = overwrite };
        args.Fingerprint = Plan(args).Fingerprint;
        string[] before = [.. _numberer.Connections.Select(c => c.Designation ?? "-")];
        _transactions.Snapshot = () =>
        {
            Assert.Single(_backup.Copies);
            return () => { };
        };

        ConnectionNumberData data = Execute(args);

        Assert.Equal(("Standard", overwrite), Assert.Single(_numberer.Designations));
        Assert.Equal(["Begin AI: eplan_connection_number", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal(renumbered.Select(id => _numberer.Numbers[id]), data.Changed.Select(c => c.To!));
        Assert.Equal(renumbered.Select(id => before[int.Parse(id[1..], System.Globalization.CultureInfo.InvariantCulture) - 1]), data.Changed.Select(c => c.From ?? "-"));
        Assert.Equal((renumbered.Length, designatedAfter, null), (data.ChangedCount, data.DesignatedAfter, data.Warning));
    }

    [Fact]
    public void Execute_ASchemeEplanRefuses_RollsBackWithTheSchemesEplanNamed()
    {
        var args = new ConnectionNumberArgs { Project = "Line1", Scheme = "Standard" };
        args.Fingerprint = Plan(args).Fingerprint;
        _numberer.Refuse = true;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_WhenAWireNumberChangedOrTheBackupFails_NothingIsNumbered()
    {
        var args = new ConnectionNumberArgs { Project = "Line1", Scheme = "Standard" };
        args.Fingerprint = Plan(args).Fingerprint;
        _numberer.Connections[3].Designation = "250";
        BridgeError stale = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        _numberer.Connections[3].Designation = "200";
        _backup.Failure = new IOException("disk full");

        BridgeError failed = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (stale.Reason, stale.Execution));
        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (failed.Reason, failed.Execution));
        Assert.Empty(_numberer.Designations);
    }

    [Fact]
    public void Execute_ListsAtMost200ChangedConnections_WithTheirCount()
    {
        _numberer.Connections.AddRange(Enumerable.Range(1, 250).Select(i => new DesignatedConnection { Id = "n" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        foreach (DesignatedConnection connection in _numberer.Connections.Skip(5))
        {
            _numberer.Numbers[connection.Id] = connection.Id;
        }

        var args = new ConnectionNumberArgs { Project = "Line1", Scheme = "Standard" };
        args.Fingerprint = Plan(args).Fingerprint;
        ConnectionNumberData many = Execute(args);

        Assert.Equal((200, 253), (many.Changed.Count, many.ChangedCount));
        Assert.Null(many.Warning);
    }

    [Fact]
    public void Execute_WhenEplanGivesNewIdentifiers_SaysSoInsteadOfGuessing()
    {
        var args = new ConnectionNumberArgs { Project = "Line1", Scheme = "Standard" };
        args.Fingerprint = Plan(args).Fingerprint;
        _numberer.NewIds = true;

        ConnectionNumberData data = Execute(args);

        Assert.Empty(data.Changed);
        Assert.StartsWith("EPLAN gave 5 connections new identifiers", data.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Changes_AreByConnection_WithThePageAfterwards_AndCountConnectionsWithoutAMatch()
    {
        DesignatedConnection[] before = [new() { Id = "1", Designation = "*", Page = "=A/1" }, new() { Id = "2" }, new() { Id = "3", Designation = "7" }, new() { Id = "4", Designation = "8" }];
        DesignatedConnection[] after = [new() { Id = "1", Designation = "433", Page = "=A/1" }, new() { Id = "2", Designation = "011" }, new() { Id = "3", Designation = "7" }];

        (List<NumberedConnection> changed, int unmatched) = ConnectionNumberRequest.Changes(before, after);

        Assert.Equal([("*", "433", "=A/1"), (null, "011", null)], changed.Select(c => (c.From, c.To, c.Page)));
        Assert.Equal(1, unmatched);
    }

    private WritePlan Plan(ConnectionNumberArgs args) => (WritePlan)new ConnectionNumberPlanOperation(new FakeCatalog(_project), _numberer).Execute(args)!;

    private ConnectionNumberData Execute(ConnectionNumberArgs args)
    {
        args.Backup = new BackupTarget { Path = Path.Combine(_folder, "Line1.elk"), MaxSeconds = 150 };
        return (ConnectionNumberData)new ConnectionNumberExecuteOperation(new FakeCatalog(_project), _pages, _numberer, _backup, _transactions).Execute(args)!;
    }
}
