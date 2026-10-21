using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;

namespace EplanMcp.Tests;

/// <summary>
/// Reads an audit log the way a reader beside a running Host has to. <see cref="AuditLog"/> appends with
/// <c>FileShare.Read</c>, so the file is opened sharing write (and delete): reading with <c>File.ReadAllLines</c>, which
/// shares only reading, fails while the Host appends and makes the Host's next append fail while the test reads. A last
/// line without its newline is a record the Host is still appending and is left out. Linked into the test projects
/// that read an audit log.
/// </summary>
internal static class AuditFiles
{
    /// <summary>Every complete record in the audit directory <paramref name="directory"/>, oldest first.</summary>
    public static List<JsonObject> Records(string directory)
    {
        var records = new List<JsonObject>();
        if (!Directory.Exists(directory))
        {
            return records;
        }

        foreach (string file in Directory.EnumerateFiles(directory, AuditLog.FilePattern).Order(StringComparer.Ordinal))
        {
            string text;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }

            // AuditLog ends every record with "\n"; whatever follows the last one has not been written in full yet.
            string complete = text[..(text.LastIndexOf('\n') + 1)];
            records.AddRange(complete.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()));
        }

        return records;
    }
}
