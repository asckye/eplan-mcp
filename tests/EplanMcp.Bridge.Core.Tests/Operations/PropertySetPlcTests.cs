using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertySetPlcTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData("PLCBox", true, 20161)]
    [InlineData("PLCBox", true, 20408)]
    [InlineData("PLCBox", true, 20409)]
    [InlineData("PLCBox", true, 20410)]
    [InlineData("PLCBox", true, 20411)]
    [InlineData("PLCBox", true, 20427)]
    [InlineData("PLCBox", true, 20308)]
    [InlineData("PLCTerminal", false, 20400)]
    [InlineData("PLCTerminal", false, 20402)]
    [InlineData("PLCTerminal", false, 20407)]
    [InlineData("PLCTerminal", false, 20011)]
    [InlineData("BusTerminal", false, 20400)]
    [InlineData("BusTerminal", false, 20402)]
    [InlineData("BusTerminal", false, 20407)]
    [InlineData("BusTerminal", false, 20011)]
    public void PublishedCategory_AssignsExactLocatedPointAndCommits(string category, bool main, int id)
    {
        using FakePropertySetSession session = Session(category, main, id);
        PropertySetChange change = Change(session, id);
        var args = new PropertySetArgs { Project = PropertySetPlannerTests.Project, Changes = [change], Approved = session.Plan([change]) };
        var transactions = new FakeTransactions();
        Assert.Equal(ExecutionState.Committed, PropertySetExecuteTests.Execute(session, transactions, args).Execution);
        Assert.Equal(1, session.Writes);
        Assert.Equal("Commit", transactions.Calls[1]);
        if (id == 20011)
        {
            Assert.Equal("new", session.Objects[1].Properties[id].Texts["zh_CN"]);
        }
        else
        {
            Assert.Equal(id == 20308 ? (object)12L : "new", session.Objects[1].Properties[id].Stored);
        }
        Assert.Equal(id == 20308 ? (object)0L : "old", session.Objects[2].Properties[id].Stored);
    }

    [Theory]
    [InlineData("Coil", false, 20011)]
    [InlineData("Terminal", false, 20011)]
    [InlineData("PLCBox", false, 20161)]
    [InlineData("PLCBox", true, 20400)]
    [InlineData("PLCTerminal", true, 20400)]
    [InlineData("BusTerminal", false, 20027)]
    public void OtherCategoriesAndMainFlags_RemainDenied(string category, bool main, int id)
    {
        using FakePropertySetSession session = Session(category, main, id);
        PropertySetChange change = Change(session, id);
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => session.Plan([change])).Error.Reason);
        Assert.Equal(0, session.Writes);
    }

    [Fact]
    public void SameNameRj45WithDifferentPlugs_OnlyExactLocatorChanges()
    {
        using FakePropertySetSession session = Session("BusTerminal", false, 20400);
        session.Inventory.ForEach(f => f.Name = "=GAA+A1-A1:RJ45");
        session.Inventory[0].Pos = "100,100";
        session.Inventory[1].Pos = "120,100";
        session.Objects[1].Properties[20406] = new FakeProperty { Stored = "X1" };
        session.Objects[2].Properties[20406] = new FakeProperty { Stored = "X2" };
        PropertySetChange change = Change(session, 20400);
        var args = new PropertySetArgs { Project = PropertySetPlannerTests.Project, Changes = [change], Approved = session.Plan([change]) };
        Assert.Equal(ExecutionState.Committed, PropertySetExecuteTests.Execute(session, new(), args).Execution);
        Assert.Equal("old", session.Objects[2].Properties[20400].Stored);
        Assert.Equal("X2", session.Objects[2].Properties[20406].Stored);
    }

    [Fact]
    public void NonMainBusinessAddress_CannotChooseAnArbitraryPoint()
    {
        using FakePropertySetSession session = Session("BusTerminal", false, 20400);
        PropertySetChange change = Change(session, 20400);
        change.Target = new PropertyTarget(deviceTag: session.Inventory[0].Name);
        Assert.Equal(ErrorReason.DeviceNotFound, Assert.Throws<BridgeErrorException>(() => session.Plan([change])).Error.Reason);
    }

    [Fact]
    public void IntegerBusSystem_DoesNotAcceptTextOrOutOfRangeValue()
    {
        using FakePropertySetSession session = Session("PLCBox", true, 20308);
        PropertySetChange change = Change(session, 20308);
        foreach (PropertyTypedValue value in new[] { new PropertyTypedValue("text", text: "12"), new("integer", integerValue: 52) })
        {
            change.Value = value;
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => session.Plan([change])).Error.Reason);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingReadData_WarnsWithoutAWrite(bool definition)
    {
        using FakePropertySetSession session = Session("PLCBox", true, 20161);
        if (definition)
        {
            session.Objects[1].Properties.Remove(20161);
        }
        else
        {
            session.Objects[1].Properties[20161].ValueApplicable = false;
        }
        PropertySetPlan plan = session.Plan([Change(session, 20161)]);
        Assert.Empty(plan.Plan.Changes);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void RelatedPointMutation_RollsBackTheBatch()
    {
        using FakePropertySetSession session = Session("PLCTerminal", false, 20400);
        PropertySetChange change = Change(session, 20400);
        var args = new PropertySetArgs { Project = PropertySetPlannerTests.Project, Changes = [change], Approved = session.Plan([change]) };
        session.AfterWrite = _ => session.Objects[2].Properties[20400].Stored = "unexpected";
        var transaction = new FakeTransactions { Snapshot = session.RestorePoint };
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => PropertySetExecuteTests.Execute(session, transaction, args));
        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
        Assert.Equal("old", session.Objects[1].Properties[20400].Stored);
        Assert.Equal("old", session.Objects[2].Properties[20400].Stored);
    }

    [Theory]
    [InlineData(20011, 20031)]
    [InlineData(20011, 20120)]
    [InlineData(20161, 20108)]
    [InlineData(20408, 20420)]
    [InlineData(20410, 20421)]
    [InlineData(20411, 20422)]
    public void KnownReadOnlyProjection_MayFollowItsSource(int id, int indirect)
    {
        using FakePropertySetSession session = Session(id == 20011 ? "PLCTerminal" : "PLCBox", id != 20011, id);
        session.Objects[2].Properties[indirect] = new FakeProperty { Stored = "old" };
        PropertySetChange change = Change(session, id);
        var args = new PropertySetArgs { Project = PropertySetPlannerTests.Project, Changes = [change], Approved = session.Plan([change]) };
        session.AfterWrite = _ => session.Objects[2].Properties[indirect].Stored = "new";
        Assert.Equal(ExecutionState.Committed, PropertySetExecuteTests.Execute(session, new(), args).Execution);
        Assert.Equal("new", session.Objects[2].Properties[indirect].Stored);
    }

    [Fact]
    public void UnrequestedDirectFieldOnAnotherCard_RollsBack()
    {
        using FakePropertySetSession session = Session("PLCBox", true, 20161);
        session.Inventory[1].Name = "=GAA+A1-A2";
        session.Objects[2].Properties[20427] = new FakeProperty
        {
            Stored = "old",
            Metadata = new PropertyDefinitionMetadata
            { ValueType = "text", IsIndexed = false, IsInternal = false, IsReadOnly = false, IsNamePart = false, IsPropertyLicensed = true },
        };
        PropertySetChange change = Change(session, 20161);
        var args = new PropertySetArgs { Project = PropertySetPlannerTests.Project, Changes = [change], Approved = session.Plan([change]) };
        session.AfterWrite = _ => session.Objects[2].Properties[20427].Stored = "unrequested";
        var transaction = new FakeTransactions { Snapshot = session.RestorePoint };
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => PropertySetExecuteTests.Execute(session, transaction, args)).Error.Execution);
        Assert.Equal("old", session.Objects[2].Properties[20427].Stored);
    }

    [Fact]
    [Trait("Size", "Medium")]
    public void B8TypedPayload_RoundTripsBothPipeSerializers()
    {
        using FakePropertySetSession session = Session("PLCBox", true, 20308);
        PropertySetChange change = Change(session, 20308);
        PropertySetChange copied = JsonSerializer.Deserialize<PropertySetChange>(EplanMcp.Bridge.Core.Protocol.BridgeJson.FromObject(change).ToString(), _jsonOptions)!;
        Assert.Equal(12, copied.Value.IntegerValue);
        Assert.Equal(session.Inventory[0].Oid, copied.Target.Locator!.Oid);
        Assert.Single(session.Plan([copied]).Plan.Changes);
    }

    private static FakePropertySetSession Session(string category, bool main, int id)
    {
        var session = new FakePropertySetSession();
        foreach (SnapshotObject obj in session.Inventory)
        {
            obj.Category = category;
            obj.Main = main;
            obj.Name = category == "PLCBox" ? "=GAA+A1-A1" : "=GAA+A1-A1:1.1";
            session.Objects[obj.Oid].Properties[id] = new FakeProperty
            {
                Stored = id == 20308 ? (object)0L : "old",
                Display = "old",
                Metadata = new PropertyDefinitionMetadata
                {
                    ValueType = id == 20011 ? "multi_language" : id == 20027 ? "opaque" : id == 20308 ? "integer" : "text",
                    Format = id == 20027 ? "ValueWithUnit" : null,
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
        Value = id == 20308 ? new("integer", integerValue: 12) : new(id == 20011 ? "multi_language" : "text", text: "new"),
    };
}
