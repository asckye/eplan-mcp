using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>SEC-06 against threat T2: a requestState is honoured only as signed, once, for its own call, in time.</summary>
[Trait("Size", "Small")]
public sealed class PendingPlanStoreTests
{
    private const string Tool = "eplan_page_create";

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
    private readonly PendingPlanStore _store;
    private readonly string _args = PendingPlanStore.HashArguments(new { project = "D:\\Line1", pages = new[] { "=A1&EFS1/5" } });
    private readonly WritePlan _plan = new() { Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }], Fingerprint = "f" };

    public PendingPlanStoreTests() => _store = new PendingPlanStore(_clock);

    [Fact]
    public void Take_TheStateItSigned_GivesThePlanBackOnce()
    {
        string state = _store.Add(Tool, "D:\\Line1", _args, _plan);

        (PendingPlan? plan, PlanRejection? rejection) = _store.Take(state, Tool, _args);

        Assert.Null(rejection);
        Assert.Same(_plan, plan!.Plan);
        Assert.Equal("D:\\Line1", plan.Project);
        Assert.Equal((null, PlanRejection.Expired), _store.Take(state, Tool, _args)); // replay
    }

    [Fact]
    public void Take_AfterFiveMinutes_HasExpired()
    {
        string state = _store.Add(Tool, "D:\\Line1", _args, _plan);
        _clock.Now += PendingPlanStore.Lifetime;

        Assert.Equal((null, PlanRejection.Expired), _store.Take(state, Tool, _args));
    }

    [Fact]
    public void Take_ForAnotherToolOrOtherArguments_IsAMismatch()
    {
        string state = _store.Add(Tool, "D:\\Line1", _args, _plan);

        Assert.Equal(PlanRejection.Mismatch, _store.Take(state, "eplan_page_update", _args).Rejection);
        Assert.Equal(PlanRejection.Mismatch, _store.Take(state, Tool, PendingPlanStore.HashArguments(new { project = "D:\\Line2" })).Rejection);
    }

    [Fact]
    public void Take_AChangedOrForeignState_IsInvalid()
    {
        string state = _store.Add(Tool, "D:\\Line1", _args, _plan);
        string[] parts = state.Split('.');
        string otherHost = new PendingPlanStore(_clock).Add(Tool, "D:\\Line1", _args, _plan);

        // Flip the payload's first character, sign nothing new: the HMAC no longer matches.
        string tampered = (parts[0][0] == 'a' ? "b" : "a") + parts[0][1..] + "." + parts[1];

        string forged = parts[0] + "." + (parts[1][0] == 'A' ? "B" : "A") + parts[1][1..];

        Assert.All([tampered, forged, otherHost, "", "x", "a.b.c", "!!!.???"], s => Assert.Equal(PlanRejection.Invalid, _store.Take(s, Tool, _args).Rejection));
        Assert.Null(_store.Take(state, Tool, _args).Rejection); // the real one still works
    }

    [Fact]
    public void HashArguments_DependsOnEveryValue()
    {
        Assert.Equal(PendingPlanStore.HashArguments(new { a = 1, b = "x" }), PendingPlanStore.HashArguments(new { a = 1, b = "x" }));
        Assert.NotEqual(PendingPlanStore.HashArguments(new { a = 1, b = "x" }), PendingPlanStore.HashArguments(new { a = 1, b = "y" }));
    }

    [Fact]
    public void Add_KeepsAtMostTheCapacity()
    {
        string first = _store.Add(Tool, "D:\\Line1", _args, _plan);
        for (int i = 0; i < PendingPlanStore.Capacity; i++)
        {
            _clock.Now += TimeSpan.FromMilliseconds(1);
            _store.Add(Tool, "D:\\Line1", _args, _plan);
        }

        Assert.Equal(PlanRejection.Expired, _store.Take(first, Tool, _args).Rejection);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
