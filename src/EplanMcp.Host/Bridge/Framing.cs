using System.Globalization;
using System.Text;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// Host side of the <c>Content-Length</c> framing (ADR-0003). Mirrors Bridge.Core's implementation, which the Host
/// cannot reference (it targets EPLAN's runtime and Newtonsoft); the Medium interop tests keep the two in step.
/// </summary>
internal static class Framing
{
    private const string ContentLength = "Content-Length:";
    private const int MaxHeaderLineBytes = 1024;

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        byte[] frame = new byte[header.Length + body.Length];
        header.CopyTo(frame, 0);
        body.CopyTo(frame.AsMemory(header.Length));
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one frame body; throws <see cref="IOException"/> on end of stream or broken framing.</summary>
    public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        int? length = null;
        while (true)
        {
            string line = await ReadHeaderLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith(ContentLength, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan(ContentLength.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                length = parsed;
            }
        }

        if (length is null or > BridgeProtocol.MaxFrameBytes)
        {
            throw new IOException("The Bridge sent a frame without a valid Content-Length.");
        }

        byte[] body = new byte[length.Value];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return body;
    }

    private static async Task<string> ReadHeaderLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        byte[] one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new EndOfStreamException("The Bridge closed the connection.");
            }

            if (one[0] == (byte)'\n' && bytes.Count > 0 && bytes[^1] == (byte)'\r')
            {
                return Encoding.ASCII.GetString([.. bytes[..^1]]);
            }

            bytes.Add(one[0]);
            if (bytes.Count > MaxHeaderLineBytes)
            {
                throw new IOException("The Bridge sent an over-long frame header.");
            }
        }
    }
}
