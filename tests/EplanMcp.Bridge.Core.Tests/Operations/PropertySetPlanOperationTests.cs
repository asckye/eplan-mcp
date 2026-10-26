using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertySetPlanOperationTests
{
    [Fact]
    public void Plan_B1_CopiesEvidenceAndDisposesTheWorkUnitWithoutWriting()
    {
        using var session = new FakePropertySetSession();
        var operation = new PropertySetPlanOperation(project =>
        {
            Assert.Equal(PropertySetPlannerTests.Project, project);
            return session;
        }, 2026);

        PropertySetPlan plan = Assert.IsType<PropertySetPlan>(operation.Execute(PropertySetExecuteTests.Args()));

        Assert.Equal(OperationNames.PropertySetPlan, operation.Name);
        Assert.False(typeof(IWriteOperation).IsAssignableFrom(operation.GetType()));
        Assert.Equal(ExecutionState.NotStarted, plan.Execution);
        Assert.Equal(2, plan.Plan.Changes.Count);
        Assert.Equal(4, plan.Guards.Count);
        Assert.Equal(0, session.Writes);
        Assert.Empty(PropertyWriteWhitelist.Entries);
        session.AssertDisposed();
    }

    [Theory]
    [InlineData(2026, 20024)]
    [InlineData(2026, 20022)]
    [InlineData(2026, 3012)]
    [InlineData(2027, 20011)]
    public void Plan_OutsideB1_RejectsBeforePropertyAccess(int version, int id)
    {
        using var session = new FakePropertySetSession();
        var operation = new PropertySetPlanOperation(_ => session, version);
        PropertySetArgs args = PropertySetExecuteTests.Args([PropertySetPlannerTests.Change()]);
        args.Changes[0].Property = new PropertySelector(propertyId: id);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(args));

        Assert.Equal(ErrorReason.PolicyDenied, error.Error.Reason);
        Assert.Equal(1, session.Disposals);
        Assert.Equal(0, session.Writes);
        Assert.All(session.Objects.Values, adapter => Assert.Equal(0, adapter.Opens));
    }

    [Fact]
    public void Plan_GetterException_ReleasesAllListsAndPartialResources()
    {
        using var session = new FakePropertySetSession();
        session.Objects[2].OnAccess = stage =>
        {
            if (stage == "texts.opened")
            {
                throw new InvalidOperationException("native getter failed");
            }
        };
        var operation = new PropertySetPlanOperation(_ => session, 2026);

        Assert.Throws<InvalidOperationException>(() => operation.Execute(PropertySetExecuteTests.Args()));

        Assert.Equal(0, session.Writes);
        session.AssertDisposed();
    }
}
