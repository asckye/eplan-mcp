using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Host.Audit;

/// <summary>The audit log cannot be written; writes are refused with AUDIT_UNAVAILABLE (architecture 8.8, Q7).</summary>
internal sealed class AuditUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The append-only audit log (architecture 8.8, M3 design 4.3, SEC-09): one JSONL file per UTC day in
/// <see cref="HostDataLocations.Audit"/>. Each record carries <c>prev</c>, the hash of the record before it (across
/// files), and <c>hash</c> = SHA-256(prev + the record's JSON without <c>hash</c>), so a changed or removed record breaks
/// the chain where <see cref="AuditVerifier"/> looks. Host processes take turns through an exclusive lock file.
/// </summary>
internal sealed class AuditLog(string directory, TimeProvider clock)
{
    public const string FilePattern = "????-??-??.jsonl";

    private const string LockFile = ".lock";

    // How long a Host waits for another one to finish appending before reporting the log unavailable.
    private static readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(5);

    internal static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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
            string prev = LastHash(file) ?? string.Empty;

            var record = new JsonObject { ["time"] = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture) };
            foreach ((string name, JsonNode? value) in entry)
            {
                record[name] = value?.DeepClone();
            }

            record["prev"] = prev;
            string hash = Hash(prev, record.ToJsonString(Json));
            record["hash"] = hash;

            using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
            byte[] line = Encoding.UTF8.GetBytes(record.ToJsonString(Json) + "\n");
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

    /// <summary>The newest file's last record's hash, looking back from <paramref name="file"/>; null for the very first.</summary>
    private string? LastHash(string file)
    {
        string? last = File.Exists(file) ? LastLine(file) : null;
        if (last is null)
        {
            string? previous = Directory.EnumerateFiles(directory, FilePattern)
                .Where(f => string.CompareOrdinal(Path.GetFileName(f), Path.GetFileName(file)) < 0)
                .Max(StringComparer.Ordinal);
            last = previous is null ? null : LastLine(previous);
        }

        return last is null ? null : JsonNode.Parse(last)?["hash"]?.GetValue<string>();
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
        foreach (string line in Directory.EnumerateFiles(directory, FilePattern).Order(StringComparer.Ordinal).SelectMany(File.ReadLines))
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

    internal static string? LastLine(string file) =>
        File.ReadLines(file).LastOrDefault(l => l.Length > 0);

    private async Task<FileStream> TakeTurnAsync(CancellationToken cancellationToken)
    {
        // Real time, not the clock records are stamped with: the wait is about other processes.
        string path = Path.Combine(directory, LockFile);
        long deadline = Environment.TickCount64 + (long)_lockTimeout.TotalMilliseconds;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
