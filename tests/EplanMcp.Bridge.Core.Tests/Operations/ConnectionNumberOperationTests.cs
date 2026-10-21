using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// connection.number.plan and connection.number.execute against a fake that numbers the way spike S21 found EPLAN
/// numbers the sample project's connections (api-notes A24: '*' → 433, none → 011): the scheme check, the overwrite
/// modes, the change list and the read-back by connection.
/// </summary>
[Trait("Size", "Small")]
public sealed class ConnectionNumberOperationTests
{
    private const string Page = "=F+A2&EFS1/2";

    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly FakeConnectionNumberer _numberer = new();

    public ConnectionNumberOperationTests()
    {
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
}
