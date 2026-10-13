using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Spikes.S4;

/// <summary>
/// Appends every newline-delimited JSON-RPC message seen on the stdio transport to a JSONL file as
/// <c>{"t": time, "dir": "in"|"out", "msg": message}</c>. Thread-safe: reads and writes happen on different threads.
/// </summary>
internal sealed class WireLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private readonly Dictionary<string, MemoryStream> _pending = new(StringComparer.Ordinal);

    public WireLog(string path)
    {
        _writer = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    public void Append(string direction, ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(direction, out MemoryStream? buffer))
            {
                buffer = new MemoryStream();
                _pending[direction] = buffer;
            }

            foreach (byte b in bytes)
            {
                if (b == (byte)'\n')
                {
                    WriteLine(direction, Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimEnd('\r'));
                    buffer.SetLength(0);
                }
                else
                {
                    buffer.WriteByte(b);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
            foreach (MemoryStream buffer in _pending.Values)
            {
                buffer.Dispose();
            }
        }
    }

    private void WriteLine(string direction, string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        JsonNode? message;
        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            message = JsonValue.Create(line); // not JSON: keep the raw text, it is evidence too
        }

        var entry = new JsonObject
        {
            ["t"] = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            ["dir"] = direction,
            ["msg"] = message,
        };
        _writer.WriteLine(entry.ToJsonString());
    }
}
