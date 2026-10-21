using System.Globalization;
using System.IO;
using System.Text.Json;

namespace EplanMcp.Workbench.Audit;

/// <summary>
/// One record of the audit log (architecture 8.8), as plain values: the fields the page filters by, and every field in
/// the order the file has them, for the details drawer. All of it is shown as plain text only (CTL-12).
/// </summary>
internal sealed record AuditRecord(string File, int Line, DateTimeOffset? Time, IReadOnlyList<KeyValuePair<string, string>> Fields)
{
    public string? Kind => Field("kind");

    public string? Tool => Field("tool");

    public string? Project => Field("project");

    public string? RequestId => Field("requestId");

    /// <summary>The approval's or release's decision, such as <c>granted</c>.</summary>
    public string? Decision => Field("decision");

    /// <summary>The write's result, such as <c>committed</c>.</summary>
    public string? Execution => Field("execution");

    public string? Field(string name) => Fields.FirstOrDefault(f => f.Key == name).Value;
}

/// <summary>What the audit folder held: its records, oldest first, and how many lines were not records.</summary>
internal sealed record AuditLogContents(IReadOnlyList<AuditRecord> Records, int NotRecords);

/// <summary>
/// Reads the audit log as the Host's <c>AuditLog.ReadLines</c> does (M5 design 4.4): every <c>yyyy-MM-dd.jsonl</c> in
/// file-name order, each file opened sharing reading, writing and deletion, so that a Host appending meanwhile is never
/// blocked; no lock is taken and nothing is ever written. Whatever follows a file's last "\n" may be a record still
/// being written and is left out. Lines that are not JSON objects are counted, not shown: audit verify reports them.
/// </summary>
internal static class AuditReader
{
    public const string FilePattern = "????-??-??.jsonl";

    private static readonly JsonDocumentOptions _strict = new() { AllowDuplicateProperties = false };

    public static AuditLogContents Read(string directory, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string[] files = Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory, FilePattern).Order(StringComparer.Ordinal)] : [];
        var records = new List<AuditRecord>();
        int notRecords = 0, reported = 0;
        for (int i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int line = 0;
            foreach (string text in ReadLines(files[i]))
            {
                line++;
                if (Record(Path.GetFileName(files[i]), line, text) is { } record)
                {
                    records.Add(record);
                }
                else if (text.Length > 0)
                {
                    notRecords++;
                }
            }

            // At most a hundred reports, however many days the log holds: each one is work on the UI thread.
            if ((i + 1) * 100 / files.Length is var percent && percent > reported)
            {
                reported = percent;
                progress?.Report(percent / 100.0);
            }
        }

        return new AuditLogContents(records, notRecords);
    }

    /// <summary>The complete lines of <paramref name="file"/>, in order; see the class.</summary>
    internal static IEnumerable<string> ReadLines(string file)
    {
        string text;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            text = reader.ReadToEnd();
        }

        using var lines = new StringReader(text[..(text.LastIndexOf('\n') + 1)]);
        while (lines.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    /// <summary>The line as a record: a JSON object, each field once; values other than strings as the file writes them.</summary>
    private static AuditRecord? Record(string file, int line, string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text, _strict);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var fields = new List<KeyValuePair<string, string>>();
            foreach (JsonProperty field in json.RootElement.EnumerateObject())
            {
                fields.Add(KeyValuePair.Create(field.Name, field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString()! : field.Value.GetRawText()));
            }

            DateTimeOffset? time = fields.FirstOrDefault(f => f.Key == "time").Value is { } stamp
                && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : null;
            return new AuditRecord(file, line, time, fields);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null; // not JSON, a field twice, or a name or value holding half a surrogate pair
        }
    }
}
