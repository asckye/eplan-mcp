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

/// <summary>Where <see cref="AuditVerifier.Verify"/> found the chain broken: the file name and the 1-based line.</summary>
internal sealed record AuditBreak(string File, int Line, AuditProblem Problem);

/// <summary>
/// Checks the whole hash chain of <see cref="AuditLog"/>, file by file in date order (Q9, SEC-09). It finds a record
/// that was changed, removed or inserted before the last one; records removed from the end leave no trace in the chain.
/// A last line without its "\n" is a record another Host is still appending and is not checked.
/// </summary>
internal static class AuditVerifier
{
    public static (int Records, AuditBreak? Break) Verify(string directory)
    {
        string prev = string.Empty;
        int records = 0;
        foreach (string file in Directory.EnumerateFiles(directory, AuditLog.FilePattern).Order(StringComparer.Ordinal))
        {
            int line = 0;
            foreach (string text in AuditLog.ReadLines(file))
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
