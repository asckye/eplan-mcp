using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanCapacityTests
{
    [Theory]
    [InlineData(0, 16, 16, false)]
    [InlineData(1, 16, 16, true)]
    [InlineData(496, 16, 512, false)]
    [InlineData(497, 16, 512, true)]
    [InlineData(999, 1, 1000, false)]
    [InlineData(1000, 1, 1000, true)]
    public void Admission_ReservesTheEntireLimit(long used, long added, long limit, bool exhausted)
    {
        if (exhausted)
        {
            PlanCapacityException error = Assert.Throws<PlanCapacityException>(() => PlanCapacity.Require("plans.bytes", used, added, limit));
            Assert.Equal("RESOURCE_EXHAUSTED", error.Reason);
            Assert.False(error.Retryable);
            Assert.Equal("plans.bytes", error.Metadata["resource"]!.GetValue<string>());
            Assert.Equal(limit, error.Metadata["limit"]!.GetValue<long>());
            Assert.Equal(used, error.Metadata["used"]!.GetValue<long>());
        }
        else
        {
            PlanCapacity.Require("plans.bytes", used, added, limit);
        }
    }

    [Fact]
    public void Retention_UsesLaterOfExpiryAndCompletion_WithExactThirtyDayBoundary()
    {
        PlanDefinition definition = PlanTestData.Definition();
        var clock = new PlanTestClock(definition.ExpiresAt.AddDays(30).AddTicks(-1));
        var record = new PlanRecord(1, "hash", definition, new());
        Assert.True(PlanCapacity.Retained(record, clock.GetUtcNow()));
        clock.Now = clock.Now.AddTicks(1);
        Assert.False(PlanCapacity.Retained(record, clock.GetUtcNow()));
        PlanState complete = new PlanState().Admit("a", "t", "h").Complete(PlanResult.Aggregate([]), definition.ExpiresAt.AddHours(1));
        Assert.True(PlanCapacity.Retained(record with { State = complete }, clock.GetUtcNow()));
        clock.Now = complete.CompletedAt!.Value.AddDays(30);
        Assert.False(PlanCapacity.Retained(record with { State = complete }, clock.GetUtcNow()));
    }

    [Theory]
    [InlineData("Compiled")]
    [InlineData("Approved")]
    [InlineData("Running")]
    [InlineData("Unknown")]
    public void Retention_ActiveAndUnknownAreNeverEvicted(string status)
    {
        var record = new PlanRecord(1, "hash", PlanTestData.Definition(), new(Status: Enum.Parse<PlanStatus>(status), AttemptId: "occupied"));
        Assert.True(PlanCapacity.Retained(record, DateTimeOffset.MaxValue));
    }

    [Fact]
    public void Parts_DefinitionStateAndEvidenceHaveIndependentLimits()
    {
        PlanDefinition definition = PlanTestData.Definition();
        long definitionBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(definition, PlanJson.Options).LongLength;
        PlanCapacity.Parts(definition, definitionBytes + PlanCapacity.MiB, 13 * PlanCapacity.MiB);
        Assert.Throws<PlanCapacityException>(() => PlanCapacity.Parts(definition, definitionBytes + PlanCapacity.MiB + 1, 0));
        Assert.Throws<PlanCapacityException>(() => PlanCapacity.Parts(definition, definitionBytes, 13 * PlanCapacity.MiB + 1));
        PlanDefinition large = definition with { EplanVersion = new string('a', (int)(2 * PlanCapacity.MiB)) };
        Assert.Throws<PlanCapacityException>(() => PlanCapacity.Parts(large, 0, 0));
    }
}

internal sealed class PlanTestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
