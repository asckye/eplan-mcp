using System.Text;
using EplanMcp.Bridge.Core.Protocol;

namespace EplanMcp.Bridge.Core.Tests.Protocol;

[Trait("Size", "Small")]
public sealed class FramingTests
{
    private const int Max = 1024;

    [Fact]
    public async Task WriteThenRead_ReturnsTheSameBody()
    {
        using var stream = new MemoryStream();
        byte[] body = Encoding.UTF8.GetBytes("{\"a\":\"中文\"}");

        await Framing.WriteFrameAsync(stream, body, TestContext.Current.CancellationToken);
        stream.Position = 0;

        Assert.Equal(body, await Framing.ReadFrameAsync(stream, Max, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Write_UsesContentLengthInBytesNotCharacters()
    {
        using var stream = new MemoryStream();
        await Framing.WriteFrameAsync(stream, Encoding.UTF8.GetBytes("中"), TestContext.Current.CancellationToken);

        Assert.StartsWith("Content-Length: 3\r\n\r\n", Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_BackToBackFrames_ReturnsEachThenNullAtEnd()
    {
        using var stream = Stream("Content-Length: 2\r\n\r\n{}Content-Length: 4\r\n\r\nnull");

        Assert.Equal("{}", Text(await Read(stream)));
        Assert.Equal("null", Text(await Read(stream)));
        Assert.Null(await Read(stream));
    }

    [Fact]
    public async Task Read_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();

        Assert.Null(await Read(stream));
    }

    [Fact]
    public async Task Read_OtherHeadersAndHeaderCase_AreAccepted()
    {
        using var stream = Stream("content-length: 2\r\nContent-Type: application/json\r\n\r\n{}");

        Assert.Equal("{}", Text(await Read(stream)));
    }

    [Theory]
    [InlineData("Content-Type: x\r\n\r\n{}", "no Content-Length")]
    [InlineData("Content-Length: -1\r\n\r\n", "not a non-negative integer")]
    [InlineData("Content-Length: abc\r\n\r\n", "not a non-negative integer")]
    [InlineData("Content-Length: 2000\r\n\r\n", "limit")]
    [InlineData("Content-Length: 10\r\n\r\n{}", "inside a frame body")]
    [InlineData("Content-Length: 2\r\n", "inside a frame header")]
    [InlineData("Content-Len", "inside a frame header")]
    public async Task Read_BrokenFrame_ThrowsProtocolException(string raw, string expected)
    {
        using var stream = Stream(raw);

        ProtocolException ex = await Assert.ThrowsAsync<ProtocolException>(() => Read(stream));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_HeaderLineWithoutEnd_IsBounded()
    {
        using var stream = Stream(new string('x', 5000));

        await Assert.ThrowsAsync<ProtocolException>(() => Read(stream));
    }

    private static Task<byte[]?> Read(Stream stream) => Framing.ReadFrameAsync(stream, Max, TestContext.Current.CancellationToken);

    private static MemoryStream Stream(string raw) => new(Encoding.UTF8.GetBytes(raw));

    private static string Text(byte[]? body) => Encoding.UTF8.GetString(body!);
}
