using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// When a question counts as delivered to the Workbench (ADR-0014 decision 4): only once it was written whole. Until
/// then the Workbench never showed it and another channel may ask; after, a break closes it unanswered.
/// </summary>
[Trait("Size", "Small")]
public sealed class WorkbenchDeliveryTests
{
    [Fact]
    public async Task AQuestionThatCannotBeWritten_WasNeverShown_SoAnotherChannelMayAsk()
    {
        using var broken = new ScriptedStream { WriteFails = true };

        Assert.Null(await Deliver(broken));
    }

    [Fact]
    public async Task AQuestionWrittenWhole_IsClosedWhenNoAnswerCanBeRead()
    {
        using var stream = new ScriptedStream();

        ApprovalAnswer? answer = await Deliver(stream);

        Assert.Equal(new ApprovalAnswer("r1", "f1", "h1", ApprovalDecision.Cancelled), answer);
        Assert.True(stream.Written > 3);
    }

    private static Task<ApprovalAnswer?> Deliver(Stream stream)
    {
        var plan = new WritePlan { Project = "Line1", ProjectPath = @"D:\Projects\Line1", Fingerprint = "f1" };
        var question = new ApprovalQuestion("r1", "eplan_page_create", "h1", plan, [], DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2)) { Tier = ToolTier.Write, Eplan = null };
        var channel = new WorkbenchChannel("unused", "unused.exe", () => null, TimeProvider.System, NullLogger.Instance);
        return channel.DeliverAsync(stream, question, [1, 2, 3], TestContext.Current.CancellationToken);
    }

    /// <summary>A connection that takes writes (or breaks on them) and has nothing to read.</summary>
    private sealed class ScriptedStream : Stream
    {
        public bool WriteFails { get; init; }

        public int Written { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Written += WriteFails ? throw new IOException("The pipe is broken.") : count;
    }
}
