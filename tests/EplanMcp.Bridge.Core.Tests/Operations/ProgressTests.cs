using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// Progress from an operation to the connection (M4 design 4.3): a slot that keeps only the newest report, which the
/// main thread fills without waiting.
/// </summary>
[Trait("Size", "Small")]
public sealed class ProgressTests
{
    private readonly ListLog _log = new();

    [Fact]
    public void AnOperationsReports_CarryItsRequestIdAndOnlyTheNewestIsKept()
    {
        var slot = new ProgressSlot();
        OperationRouter router = Router(new DelegateOperation("x.pages", () =>
        {
            OperationContext.Current.ReportProgress(1, 3, "Exporting page 1 of 3");
            OperationContext.Current.ReportProgress(2, 3, "Exporting page 2 of 3");
            return null;
        }));

        router.Invoke("x.pages", null, "c1", slot);

        ProgressNotification? report = slot.Take();
        Assert.Equal(("c1", (int?)2, (int?)3, "Exporting page 2 of 3"), (report?.RequestId, report?.Progress, report?.Total, report?.Message));
        Assert.Null(slot.Take());
    }

    [Fact]
    public void AReportBelowAnEarlierOne_IsIgnoredButAnEqualOneIsNot()
    {
        var slot = new ProgressSlot();
        ProgressNotification? afterGoingBack = null;
        OperationRouter router = Router(new DelegateOperation("x.pages", () =>
        {
            OperationContext.Current.ReportProgress(5, message: "five");
            OperationContext.Current.ReportProgress(4, message: "back");
            afterGoingBack = slot.Take();
            OperationContext.Current.ReportProgress(5, message: "same step, new phrase");
            return null;
        }));

        router.Invoke("x.pages", null, "c1", slot);

        Assert.Equal(((int?)5, "five"), (afterGoingBack?.Progress, afterGoingBack?.Message));
        Assert.Equal("same step, new phrase", slot.Take()?.Message);
    }

    [Fact]
    public void Changed_CompletesOnAReportAndIsPendingAgainAfterTake()
    {
        var slot = new ProgressSlot();
        Task before = slot.Changed;

        slot.Post(new ProgressNotification { RequestId = "c1", Progress = 1 });

        Assert.True(before.IsCompleted);
        Assert.Equal(1, slot.Take()!.Progress);
        Assert.False(slot.Changed.IsCompleted);
    }

    [Fact]
    public void TheContext_IsCurrentOnlyWhileItsOperationRuns()
    {
        string? during = null;
        OperationRouter router = Router(new DelegateOperation("x.read", () => during = OperationContext.Current.RequestId));

        router.Invoke("x.read", null, "c1");

        Assert.Equal("c1", during);
        Assert.Equal(string.Empty, OperationContext.Current.RequestId);
    }

    private OperationRouter Router(params IOperation[] operations) => new(operations, new InlineDispatcher(), null, _log, TimeSpan.FromSeconds(10));
}
