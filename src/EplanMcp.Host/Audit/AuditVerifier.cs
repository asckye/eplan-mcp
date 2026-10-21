using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Host.Audit;

/// <summary>Why the chain breaks at a record.</summary>
internal enum AuditProblem
{
    /// <summary>The line is not a record with <c>prev</c> and <c>hash</c>.</summary>
    NotARecord,

    /// <summary><c>prev</c> is not the hash of the record before it: a record was removed or inserted.</summary>
    Missing,

    /// <summary>The record's hash does not match its content: it was changed.</summary>
    Changed,
}

/// <summary>Where <see cref="AuditVerifier.Verify(string)"/> found the chain broken: the file name and the 1-based line.</summary>
internal sealed record AuditBreak(string File, int Line, AuditProblem Problem);

/// <summary>
/// Checks the whole hash chain of <see cref="AuditLog"/>, file by file in date order (Q9, SEC-09). It finds a record
/// that was changed, removed or inserted before the last one; records removed from the end leave no trace in the chain.
/// It reads holding the appenders' lock, so a last line without its "\n" is an append that failed halfway and breaks
/// the chain. Only when another Host keeps the lock is the newest file's last such line taken for a record still being
/// appended and left out; appends go to the newest file only.
/// </summary>
internal static class AuditVerifier
{
    // An append holds the lock for milliseconds; longer means the lock is not coming free soon.
    private static readonly TimeSpan _lockWait = TimeSpan.FromSeconds(1);

    public static (int Records, AuditBreak? Break) Verify(string directory) => Verify(directory, _lockWait);

    internal static (int Records, AuditBreak? Break) Verify(string directory, TimeSpan lockWait)
    {
        using FileStream? turn = AuditLog.TryTakeTurn(directory, lockWait);
        string[] files = [.. Directory.EnumerateFiles(directory, AuditLog.FilePattern).Order(StringComparer.Ordinal)];
        string prev = string.Empty;
        int records = 0;
        foreach (string file in files)
        {
            int line = 0;
            foreach (string text in AuditLog.ReadLines(file, withIncompleteLast: turn is not null || file != files[^1]))
            {
                line++;
                if (text.Length == 0)
                {
                    continue;
                }

                AuditProblem? problem = Check(text, prev, out string hash);
                if (problem is { } found)
                {
                    return (records, new AuditBreak(Path.GetFileName(file), line, found));
                }

                prev = hash;
                records++;
            }
        }

        return (records, null);
    }

    private static AuditProblem? Check(string text, string prev, out string hash)
    {
        hash = string.Empty;
        JsonObject? record;
        try
        {
            record = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return AuditProblem.NotARecord;
        }

        if (record?["hash"] is not JsonValue hashValue || !hashValue.TryGetValue(out string? recorded)
            || record["prev"] is not JsonValue prevValue || !prevValue.TryGetValue(out string? recordedPrev))
        {
            return AuditProblem.NotARecord;
        }

        if (!string.Equals(recordedPrev, prev, StringComparison.Ordinal))
        {
            return AuditProblem.Missing;
        }

        record.Remove("hash");
        hash = recorded;
        return string.Equals(AuditLog.Hash(prev, record.ToJsonString(AuditLog.Json)), recorded, StringComparison.Ordinal)
            ? null
            : AuditProblem.Changed;
    }
}
