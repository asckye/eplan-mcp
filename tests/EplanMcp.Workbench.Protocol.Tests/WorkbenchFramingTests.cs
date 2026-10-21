using System.Text;

namespace EplanMcp.Workbench.Protocol.Tests;

/// <summary>The frames on an in-memory stream: the 1 MiB cap on both sides and the strict header (ADR-0014 decision 3).</summary>
[Trait("Size", "Small")]
public sealed class WorkbenchFramingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Frames_ReadBackAsWritten_ThenTheEndOfTheStreamIsNull()
    {
        using var stream = new MemoryStream();
        byte[] largest = new byte[WorkbenchProtocol.MaxFrameBytes];
        largest.AsSpan().Fill((byte)'x');

        await WorkbenchFraming.WriteAsync(stream, "{}"u8.ToArray(), Token);
        await WorkbenchFraming.WriteAsync(stream, largest, Token);
        stream.Position = 0;

        Assert.Equal("Content-Length: 2\r\n\r\n{}", Encoding.ASCII.GetString(stream.ToArray(), 0, 23));
        Assert.Equal("{}"u8.ToArray(), await WorkbenchFraming.ReadAsync(stream, Token));
        Assert.Equal(largest, await WorkbenchFraming.ReadAsync(stream, Token));
        Assert.Null(await WorkbenchFraming.ReadAsync(stream, Token));
    }

    [Fact]
    public async Task AFrameOverOneMebibyte_IsNotWritten()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<WorkbenchProtocolException>(() => WorkbenchFraming.WriteAsync(stream, new byte[WorkbenchProtocol.MaxFrameBytes + 1], Token));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task AFrameOverOneMebibyte_IsRefusedBeforeItsBodyIsRead()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes($"Content-Length: {WorkbenchProtocol.MaxFrameBytes + 1}\r\n\r\n{{}}"));

        await Assert.ThrowsAsync<WorkbenchProtocolException>(() => WorkbenchFraming.ReadAsync(stream, Token));

        Assert.True(stream.Position < 40, $"read {stream.Position} bytes");
    }

    [Theory]
    [InlineData("Content-Length: 2\r\nContent-Type: application/json\r\n\r\n{}")]
    [InlineData("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")]
    [InlineData("Content-Type: application/json\r\nContent-Length: 2\r\n\r\n{}")]
    [InlineData("\r\n{}")]
    [InlineData("Content-Length: -2\r\n\r\n{}")]
    [InlineData("Content-Length: +2\r\n\r\n{}")]
    [InlineData("Content-Length: 2.0\r\n\r\n{}")]
    [InlineData("Content-Length: two\r\n\r\n{}")]
    [InlineData("Content-Length: 99999999999\r\n\r\n{}")]
    [InlineData("Content-Length: 2\n\n{}")]
    [InlineData("Content-Length: 4\r\n\r\n{}")]
    [InlineData("Content-Length: 2\r\n")]
    [InlineData("Content-Len")]
    [InlineData("Content-Length:                                                                     2\r\n\r\n{}")]
    public async Task ABrokenFrame_IsAProtocolError(string frame)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(frame));

        await Assert.ThrowsAsync<WorkbenchProtocolException>(() => WorkbenchFraming.ReadAsync(stream, Token));
    }
}
