using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertySetTerminalBusbarTests
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Terminal", 20367)]
    [InlineData("Terminal", 20809)]
    [InlineData("Terminal", 20035)]
    [InlineData("Busbar", 20011)]
    public void NonMainExactLocator_WritesOnlyThatFunction(string category, int id)
    {
        using FakePropertySetSession session = Session(category, id);
        PropertySetChange change = Change(session, id);
        var args = new PropertySetArgs { Project = PropertySetPlannerTests.Project, Changes = [change], Approved = session.Plan([change]) };
        var transactions = new FakeTransactions();
        Assert.Equal(ExecutionState.Committed, PropertySetExecuteTests.Execute(session, transactions, args).Execution);
        Assert.Equal(["Begin AI: eplan_property_set", "Commit", "CloseUndo", "Dispose"], transactions.Calls);
        Assert.Equal(1, session.Writes);
        if (id == 20011)
        {
            Assert.Equal("PE", session.Objects[1].Properties[id].Texts["zh_CN"]);
            Assert.Equal("old", session.Objects[2].Properties[id].Texts["zh_CN"]);
        }
        else
        {
            Assert.Equal(id == 20035 ? (object)60L : "PE", session.Objects[1].Properties[id].Stored);
            Assert.Equal(id == 20035 ? (object)0L : "old", session.Objects[2].Properties[id].Stored);
        }
    }

    [Theory]
    [InlineData("Terminal", 20367)]
    [InlineData("Busbar", 20011)]
    public void NonMainWithoutLocator_IsNotResolvedArbitrarily(string category, int id)
    {
        using FakePropertySetSession session = Session(category, id);
        PropertySetChange change = Change(session, id);
        change.Target = new PropertyTarget(deviceTag: session.Inventory[0].Name);
        Assert.Equal(ErrorReason.DeviceNotFound, Assert.Throws<BridgeErrorException>(() => session.Plan([change])).Error.Reason);
        Assert.Equal(0, session.Writes);
    }

    [Theory]
    [InlineData("Terminal", 20011)]
    [InlineData("Terminal", 20054)]
    [InlineData("Terminal", 20044)]
    [InlineData("Busbar", 20367)]
    [InlineData("Coil", 20367)]
    public void UnpublishedCategoryProperty_RemainsDenied(string category, int id)
    {
        using FakePropertySetSession session = Session(category, id);
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => session.Plan([Change(session, id)])).Error.Reason);
        Assert.Equal(0, session.Writes);
    }

    [Theory]
    [InlineData(20367)]
    [InlineData(20809)]
    [InlineData(20035)]
    public void TerminalFields_RequireNonMainFunction(int id)
    {
        using FakePropertySetSession session = Session("Terminal", id);
        session.Inventory[0].Main = true;
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => session.Plan([Change(session, id)])).Error.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingTerminalData_WarnsWithoutViolation(bool definition)
    {
        using FakePropertySetSession session = Session("Terminal", 20367);
        if (definition)
        { session.Objects[1].Properties.Remove(20367); }
        else
        { session.Objects[1].Properties[20367].ValueApplicable = false; }
        PropertySetPlan plan = session.Plan([Change(session, 20367)]);
        Assert.Empty(plan.Plan.Changes);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void TerminalInteger_RejectsNumericText()
    {
        using FakePropertySetSession session = Session("Terminal", 20035);
        PropertySetChange change = Change(session, 20035);
        change.Value = new("text", text: "60");
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => session.Plan([change])).Error.Reason);
    }

    [Fact]
    [Trait("Size", "Medium")]
    public void TerminalInteger_RoundTripsBothSerializersWithExactLocator()
    {
        using FakePropertySetSession session = Session("Terminal", 20035);
        PropertySetChange change = Change(session, 20035);
        string json = EplanMcp.Bridge.Core.Protocol.BridgeJson.FromObject(change).ToString();
        PropertySetChange copied = System.Text.Json.JsonSerializer.Deserialize<PropertySetChange>(json, _jsonOptions)!;
        Assert.Equal(60, copied.Value.IntegerValue);
        Assert.Equal(session.Inventory[0].Oid, copied.Target.Locator!.Oid);
        Assert.Single(session.Plan([copied]).Plan.Changes);
    }

    private static FakePropertySetSession Session(string category, int id)
    {
        var session = new FakePropertySetSession();
        foreach (SnapshotObject obj in session.Inventory)
        {
            obj.Category = category;
            obj.Main = false;
            obj.Name = category == "Terminal" ? "+A1-XQ10:1" : "+A1-WE1";
            session.Objects[obj.Oid].Properties[id] = new FakeProperty
            {
                Stored = id == 20035 ? (object)0L : "old",
                Display = "old",
                Metadata = new PropertyDefinitionMetadata
                {
                    ValueType = id == 20011 ? "multi_language" : id == 20035 ? "integer" : "text",
                    IsIndexed = false,
                    IsInternal = false,
                    IsReadOnly = false,
                    IsNamePart = false,
                    IsPropertyLicensed = true,
                },
            };
            session.Objects[obj.Oid].Properties[id].Texts["zh_CN"] = "old";
        }
        return session;
    }

    private static PropertySetChange Change(FakePropertySetSession session, int id) => new()
    {
        Target = new PropertyTarget(locator: PropertySetPlannerTests.Locator(session.Inventory[0])),
        Property = new PropertySelector(propertyId: id),
        Language = id == 20011 ? "zh_CN" : null,
        Value = id == 20035 ? new("integer", integerValue: 60) : new(id == 20011 ? "multi_language" : "text", text: "PE"),
    };
}
