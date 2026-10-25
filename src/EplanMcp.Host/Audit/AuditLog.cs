using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Http;

namespace EplanMcp.Host.Audit;

/// <summary>The audit log cannot be written; writes are refused with AUDIT_UNAVAILABLE (architecture 8.8, Q7).</summary>
internal sealed class AuditUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The append-only audit log (architecture 8.8, M3 design 4.3, SEC-09): one JSONL file per UTC day in
/// <see cref="HostDataLocations.Audit"/>. Each record carries <c>prev</c>, the hash of the record before it (across
/// files), and <c>hash</c> = SHA-256(prev + the record's JSON without <c>hash</c>), so a changed or removed record breaks
/// the chain where <see cref="AuditVerifier"/> looks. Host processes take turns through an exclusive lock file.
/// </summary>
internal sealed class AuditLog(string directory, TimeProvider clock, HttpCaller? caller = null)
{
    public const string FilePattern = "????-??-??.jsonl";

    private const string LockFile = ".lock";

    internal static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// How long a Host waits for the other Hosts' turns before reporting the log unavailable. Turns queue up, so the
    /// wait covers every turn ahead of this one, each with its flush to disk.
    /// </summary>
    internal TimeSpan LockWait { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Appends <paramref name="entry"/> with <c>time</c>, <c>prev</c> and <c>hash</c> added, flushed to disk before
    /// returning; the entry object is not changed. Throws <see cref="AuditUnavailableException"/> when that fails.
    /// </summary>
    public async Task<string> AppendAsync(JsonObject entry, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(directory);
            using FileStream turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset now = clock.GetUtcNow();
            string file = Path.Combine(directory, now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");
            (string prev, bool newLine) = Tail(file);

            var record = new JsonObject { ["time"] = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture) };
            foreach ((string name, JsonNode? value) in entry)
            {
                record[name] = value?.DeepClone();
            }

            if (caller?.Address is { } address)
            {
                record["sourceAddress"] = address;
            }

            record["prev"] = prev;
            string hash = Hash(prev, record.ToJsonString(Json));
            record["hash"] = hash;

            using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
            byte[] line = Encoding.UTF8.GetBytes((newLine ? "\n" : string.Empty) + record.ToJsonString(Json) + "\n");
            await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
            return hash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AuditUnavailableException($"The audit log in {directory} cannot be written: {ex.Message}", ex);
        }
    }

    /// <summary>SHA-256, lower-case hex, of the previous hash followed by the record's JSON.</summary>
    internal static string Hash(string prev, string recordJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prev + recordJson)));

    /// <summary>
    /// The hash of the newest record, looking back from <paramref name="file"/> (empty for the very first), and whether
    /// <paramref name="file"/> ends without its "\n": a record whose append stopped just before it, which the next
    /// record must not be joined onto.
    /// </summary>
    private (string Prev, bool NewLine) Tail(string file)
    {
        if (File.Exists(file) && LastLine(file) is { } last)
        {
            return (HashOf(last, file), !EndsWithNewLine(file));
        }

        string? previous = Directory.EnumerateFiles(directory, FilePattern)
            .Where(f => string.CompareOrdinal(Path.GetFileName(f), Path.GetFileName(file)) < 0)
            .Max(StringComparer.Ordinal);
        return (previous is not null && LastLine(previous) is { } earlier ? HashOf(earlier, previous) : string.Empty, false);
    }

    // Under the lock, a last line that is not a record is an append that failed halfway, or a change. The next record
    // cannot be chained to it, so nothing more is written until it is repaired (SEC-09); audit verify reports it.
    private static string HashOf(string line, string file)
    {
        try
        {
            if (JsonNode.Parse(line)?["hash"] is JsonValue value && value.TryGetValue(out string? hash))
            {
                return hash;
            }
        }
        catch (JsonException)
        {
        }

        throw new AuditUnavailableException(
            $"The last line of {file} is not a complete audit record, for example from an append that failed halfway; 'eplanmcp audit verify' shows where. Nothing more is written until it is repaired.");
    }

    private static bool EndsWithNewLine(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
        {
            return true;
        }

        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == '\n';
    }

    /// <summary>
    /// The "execution_started" records without an "execution_result" of the same request ID, oldest first: writes
    /// whose outcome was not recorded (ADR-0010 section 2). Lines that are not records are skipped here; audit verify
    /// reports them.
    /// </summary>
    public IReadOnlyList<JsonObject> Dangling()
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var started = new List<JsonObject>();
        var finished = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in Directory.EnumerateFiles(directory, FilePattern).Order(StringComparer.Ordinal).SelectMany(f => ReadLines(f)))
        {
            JsonObject? record;
            try
            {
                record = line.Length == 0 ? null : JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }

            string? kind = record?["kind"]?.GetValue<string>();
            string? requestId = record?["requestId"]?.GetValue<string>();
            if (requestId is null)
            {
                continue;
            }

            if (kind == "execution_started")
            {
                started.Add(record!);
            }
            else if (kind == "execution_result")
            {
                finished.Add(requestId);
            }
        }

        return [.. started.Where(r => !finished.Contains(r["requestId"]!.GetValue<string>()))];
    }

    /// <summary>A complete snapshot for plan settlement; unreadable or incomplete audit means no settlement.</summary>
    internal async Task<IReadOnlyList<JsonObject>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using FileStream turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
            string current = Path.Combine(directory, clock.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");
            if (File.Exists(current))
            {
                // Even a previously published plan_result cannot advance state while the audit is unavailable.
                using var writable = new FileStream(current, FileMode.Open, FileAccess.Write, FileShare.Read);
            }
            var records = new List<JsonObject>();
            foreach (string file in Directory.EnumerateFiles(directory, FilePattern).Order(StringComparer.Ordinal))
            {
                foreach (string line in ReadLines(file, withIncompleteLast: true).Where(l => l.Length > 0))
                {
                    records.Add(JsonNode.Parse(line) as JsonObject ?? throw new JsonException("An audit record must be an object."));
                }
            }
            return records;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new AuditUnavailableException("The audit snapshot is unavailable; plan settlement must wait.", ex);
        }
    }

    /// <summary>
    /// The lines of <paramref name="file"/> that another Host process may be appending to, in order. The appender holds
    /// the file with <c>FileShare.Read</c>, so it is opened sharing write and delete: a reader that shares only reading
    /// cannot open it then, and makes the append fail while it reads. Every record ends with "\n"; what follows the
    /// last one may be a record still being written and is left out, unless <paramref name="withIncompleteLast"/>: the
    /// caller took the length under the lock (<see cref="TryTakeTurn"/>), so it is an append that failed halfway.
    /// With <paramref name="length"/>, only the file's first so many bytes are read: what it held when they were taken.
    /// </summary>
    internal static IEnumerable<string> ReadLines(string file, bool withIncompleteLast = false, long? length = null)
    {
        string text;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            byte[] bytes = new byte[length ?? stream.Length];
            int read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false); // shorter if it was cut meanwhile
            using var reader = new StreamReader(new MemoryStream(bytes, 0, read));
            text = reader.ReadToEnd();
        }

        int end = withIncompleteLast ? text.Length : text.LastIndexOf('\n') + 1;
        using var lines = new StringReader(end == text.Length ? text : text[..end]);
        while (lines.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    // Read under the lock, so a last line without its "\n" is an append that failed halfway; it is not left out, or
    // the next record would be joined onto it.
    internal static string? LastLine(string file) =>
        File.ReadLines(file).LastOrDefault(l => l.Length > 0);

    private async Task<FileStream> TakeTurnAsync(CancellationToken cancellationToken)
    {
        // Real time, not the clock records are stamped with: the wait is about other processes.
        long deadline = Environment.TickCount64 + (long)LockWait.TotalMilliseconds;
        while (true)
        {
            try
            {
                return OpenLock(directory);
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The appenders' lock for a reader that must see no append under way, or null when another Host still holds it
    /// after <paramref name="wait"/> or the lock file cannot be opened (a read-only directory).
    /// </summary>
    internal static FileStream? TryTakeTurn(string directory, TimeSpan wait)
    {
        long deadline = Environment.TickCount64 + (long)wait.TotalMilliseconds;
        while (true)
        {
            try
            {
                return OpenLock(directory);
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(20);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private static FileStream OpenLock(string directory) =>
        new(Path.Combine(directory, LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
