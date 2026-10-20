using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class OutcomeRecordsTests
{
    private readonly OutcomeRecords _records = new();

    [Fact]
    public void AWrite_GoesFromQueuedToItsFinalState()
    {
        Assert.True(_records.TryBegin("r1", "page.create.execute", out _));
        Assert.Equal((OutcomeStatus.Queued, "page.create.execute"), Status("r1"));

        _records.Update("r1", OutcomeStatus.Running);
        Assert.Equal(OutcomeStatus.Running, _records.Get("r1").Status);

        _records.Update("r1", OutcomeStatus.RolledBack, ErrorReason.EplanOperationFailed);
        OutcomeGetData data = _records.Get("r1");
        Assert.Equal((OutcomeStatus.RolledBack, ErrorReason.EplanOperationFailed), (data.Status, data.Reason));
    }

    [Fact]
    public void TryBegin_ARequestIdSeenBefore_IsRefusedWithWhatIsKnown()
    {
        _records.TryBegin("r1", "page.create.execute", out _);
        _records.Update("r1", OutcomeStatus.Committed);

        Assert.False(_records.TryBegin("r1", "page.create.execute", out OutcomeGetData? previous));
        Assert.Equal(OutcomeStatus.Committed, previous!.Status);
    }

    [Fact]
    public void Get_AnIdNeverSeen_IsNotStartedAndStaysSo()
    {
        Assert.Equal((OutcomeStatus.NotStarted, null), Status("late"));

        // The request arrives after the Host gave up on it: it must not run now.
        Assert.False(_records.TryBegin("late", "page.create.execute", out OutcomeGetData? previous));
        Assert.Equal(OutcomeStatus.NotStarted, previous!.Status);
    }

    [Fact]
    public void Beyond_TheCapacity_TheOldestFinalRecordsExpire()
    {
        for (int i = 0; i <= OutcomeRecords.Capacity; i++)
        {
            string id = "r" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _records.TryBegin(id, "page.create.execute", out _);
            _records.Update(id, OutcomeStatus.Committed);
        }

        Assert.Equal(OutcomeStatus.Expired, _records.Get("r0").Status);
        Assert.Equal(OutcomeStatus.Committed, _records.Get("r1").Status);
        Assert.False(_records.TryBegin("r0", "page.create.execute", out _));
    }

    [Fact]
    public void Beyond_TheCapacity_QueuedAndRunningRecordsStay()
    {
        _records.TryBegin("running", "page.create.execute", out _);
        _records.Update("running", OutcomeStatus.Running);
        for (int i = 0; i < OutcomeRecords.Capacity + 10; i++)
        {
            _records.TryBegin("q" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "page.create.execute", out _);
        }

        Assert.Equal(OutcomeStatus.Running, _records.Get("running").Status);
        Assert.Equal(OutcomeStatus.Queued, _records.Get("q0").Status);
    }

    private (string Status, string? Operation) Status(string id)
    {
        OutcomeGetData data = _records.Get(id);
        return (data.Status, data.Operation);
    }
}
