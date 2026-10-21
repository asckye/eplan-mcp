using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Evals.Run;

/// <summary>
/// The data root of the eplanmcp the model talks to (<c>EPLANMCP_HOME</c>, ADR-0008), in the run's folder. Its
/// config.json makes the folder of the writable copies the only scratch folder and refuses writes everywhere else,
/// whatever the engineer's own config.json says: the shared fixture is never written, and no approval dialog can come
/// up that nobody would answer (M3 design 4.3). Its audit log holds every write the model made.
/// </summary>
internal static class EvalsHome
{
    /// <summary>Where the model's eplanmcp writes its exports: <c>exports\</c> in its data root (M4 design 4.4).</summary>
    public static string Exports(string home) => Path.Combine(home, "exports");

    public static void Write(string home, string scratchFolder)
    {
        Directory.CreateDirectory(home);
        var config = new JsonObject { ["scratchRoots"] = new JsonArray(scratchFolder), ["writesOutsideScratch"] = "deny" };
        File.WriteAllText(Path.Combine(home, "config.json"), config.ToJsonString());
    }

    /// <summary>
    /// The <c>execution_result</c> records in the audit log under <paramref name="home"/>, one per write that ran; -1
    /// when the log cannot be read, so the caller cannot tell.
    /// </summary>
    public static int AuditedWrites(string home)
    {
        string audit = Path.Combine(home, "audit");
        if (!Directory.Exists(audit))
        {
            return 0;
        }

        try
        {
            int count = 0;
            foreach (string file in Directory.EnumerateFiles(audit, "*.jsonl"))
            {
                // An eplanmcp still appending holds the file with FileShare.Read: share writing, or the read fails.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                {
                    count += Kind(line) == "execution_result" ? 1 : 0;
                }
            }

            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static string? Kind(string line)
    {
        try
        {
            return (JsonNode.Parse(line) as JsonObject)?["kind"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
