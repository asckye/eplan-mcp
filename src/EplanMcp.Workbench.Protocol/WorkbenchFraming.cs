using System.Globalization;
using System.Text;

namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// The framing of ADR-0003, <c>Content-Length: N\r\n\r\n</c> and N bytes of UTF-8 JSON, read strictly: that one header
/// only, and at most <see cref="WorkbenchProtocol.MaxFrameBytes"/>, refused before the body is read. Any violation is a
/// <see cref="WorkbenchProtocolException"/>.
/// </summary>
public static class WorkbenchFraming
{
    private const string ContentLength = "Content-Length:";
    private const int MaxHeaderLineBytes = 64;

    /// <summary>Writes one frame and flushes it; a body over the limit is refused before anything is written.</summary>
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        if (body.Length > WorkbenchProtocol.MaxFrameBytes)
        {
            throw new WorkbenchProtocolException($"The message is {body.Length} bytes; the limit is {WorkbenchProtocol.MaxFrameBytes}.");
        }

        byte[] frame = [.. Encoding.ASCII.GetBytes($"{ContentLength} {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n"), .. body.Span];
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one frame body; null when the stream ends before a frame starts.</summary>
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false) is not { } header)
        {
            return null;
        }

        if (!header.StartsWith(ContentLength, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(header.AsSpan(ContentLength.Length).Trim(' '), NumberStyles.None, CultureInfo.InvariantCulture, out int length)
            || await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false) is not { Length: 0 })
        {
            throw new WorkbenchProtocolException("A frame must have one header, Content-Length.");
        }

        if (length > WorkbenchProtocol.MaxFrameBytes)
        {
            throw new WorkbenchProtocolException($"The frame is {length} bytes; the limit is {WorkbenchProtocol.MaxFrameBytes}.");
        }

        byte[] body = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new WorkbenchProtocolException("The stream ended inside a frame.", ex);
        }

        return body;
    }

    /// <summary>A header line without its CRLF; null at the end of the stream before any byte of it.</summary>
    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        byte[] one = new byte[1];
        while (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 1)
        {
            if (one[0] == (byte)'\n' && bytes.Count > 0 && bytes[^1] == (byte)'\r')
            {
                return Encoding.ASCII.GetString([.. bytes[..^1]]);
            }

            bytes.Add(one[0]);
            if (bytes.Count > MaxHeaderLineBytes)
            {
                throw new WorkbenchProtocolException("A frame header line is too long.");
            }
        }

        return bytes.Count == 0 ? null : throw new WorkbenchProtocolException("The stream ended inside a frame header.");
    }
}
