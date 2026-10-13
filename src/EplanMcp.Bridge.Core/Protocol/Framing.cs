using System.Globalization;
using System.Text;

namespace EplanMcp.Bridge.Core.Protocol;

/// <summary>
/// Reads and writes <c>Content-Length: N\r\n\r\n&lt;N bytes of UTF-8 JSON&gt;</c> frames, the LSP framing chosen in
/// ADR-0003. Other headers are ignored, as in LSP. Any violation is a <see cref="ProtocolException"/>.
/// </summary>
public static class Framing
{
    private const string ContentLength = "Content-Length:";
    private const int MaxHeaderLineBytes = 1024;

    /// <summary>Reads one frame body; returns null when the stream ends cleanly before a frame starts.</summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        int? length = null;
        bool started = false;
        while (true)
        {
            string? line = await ReadHeaderLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return started ? throw new ProtocolException("The stream ended inside a frame header.") : null;
            }

            started = true;
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith(ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                string value = line.Substring(ContentLength.Length).Trim();
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
                {
                    throw new ProtocolException("Content-Length is not a non-negative integer.");
                }

                length = parsed;
            }
        }

        if (length is null)
        {
            throw new ProtocolException("The frame has no Content-Length header.");
        }

        if (length.Value > maxBytes)
        {
            throw new ProtocolException($"The frame is {length.Value} bytes; the limit is {maxBytes}.");
        }

        byte[] body = new byte[length.Value];
        int read = 0;
        while (read < body.Length)
        {
            int n = await stream.ReadAsync(body, read, body.Length - read, cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                throw new ProtocolException("The stream ended inside a frame body.");
            }

            read += n;
        }

        return body;
    }

    /// <summary>Writes one frame and flushes it.</summary>
    public static async Task WriteFrameAsync(Stream stream, byte[] body, CancellationToken cancellationToken)
    {
        byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n");
        byte[] frame = new byte[header.Length + body.Length];
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        Buffer.BlockCopy(body, 0, frame, header.Length, body.Length);
        await stream.WriteAsync(frame, 0, frame.Length, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads up to CRLF; returns null at end of stream when nothing was read.</summary>
    private static async Task<string?> ReadHeaderLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        byte[] one = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(one, 0, 1, cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                return bytes.Count == 0 ? null : throw new ProtocolException("The stream ended inside a frame header.");
            }

            if (one[0] == (byte)'\n' && bytes.Count > 0 && bytes[bytes.Count - 1] == (byte)'\r')
            {
                bytes.RemoveAt(bytes.Count - 1);
                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            bytes.Add(one[0]);
            if (bytes.Count > MaxHeaderLineBytes)
            {
                throw new ProtocolException("A frame header line is too long.");
            }
        }
    }
}
