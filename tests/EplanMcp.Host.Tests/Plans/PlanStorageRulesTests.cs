using System.Text.Json;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanStorageRulesTests
{
    [Theory]
    [InlineData("../x")]
    [InlineData("C:\\x")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdef.json")]
    public void Id_IsNotAPath(string id) => Assert.Throws<ArgumentException>(() => PlanPaths.Id(id));

    [Fact]
    public void StateMachine_OnlyOneLegalRevisionIsPersisted()
    {
        var initial = new PlanState();
        PlanState admitted = initial.Admit("attempt", "task", "host");
        PlanStorageRules.Transition(initial, admitted);
        PlanState running = admitted.Start(PlanTestData.Created, PolicyVerdict.Allow);
        PlanStorageRules.Transition(admitted, running);
        PlanState done = running.Complete(PlanResult.Aggregate([]), PlanTestData.Created);
        PlanStorageRules.Transition(running, done);
        Assert.Throws<InvalidOperationException>(() => PlanStorageRules.Transition(initial, admitted with { Revision = 2 }));
        Assert.Throws<InvalidOperationException>(() => PlanStorageRules.Transition(done, running));
    }

    [Fact]
    public void Record_RejectsTamperedIdentityHashAndState()
    {
        PlanDefinition definition = PlanTestData.Definition();
        var record = new PlanRecord(1, PlanCanonical.Hash(definition), definition, new());
        PlanStorageRules.Validate(record, definition.PlanId);
        Assert.Throws<JsonException>(() => PlanStorageRules.Validate(record with { PlanHash = "changed" }, definition.PlanId));
        Assert.Throws<JsonException>(() => PlanStorageRules.Validate(record, new string('a', 32)));
        Assert.Throws<JsonException>(() => PlanStorageRules.Validate(record with { State = new(Status: PlanStatus.Committed) }, definition.PlanId));
        Assert.Throws<NotSupportedException>(() => PlanStorageRules.Validate(record with { SchemaVersion = 2 }, definition.PlanId));
    }
}
