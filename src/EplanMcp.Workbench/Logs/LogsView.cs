using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using EplanMcp.Workbench.Approvals;

namespace EplanMcp.Workbench.Logs;

internal sealed record LogRow(string Time, string Level, string Source, string Message)
{
    /// <summary>The level with the design's glyph: "✕ ERROR", "! WARN", "• INFO".</summary>
    public string LevelText => (Level switch { "ERROR" => "✕ ", "WARN" => "! ", _ => "• " }) + Level;

    /// <summary>The tone the level's text takes.</summary>
    public string Tone => Level switch { "ERROR" => "Error", "WARN" => "Warning", _ => string.Empty };
}

/// <summary>
/// Reads Workbench and Bridge runtime log files from <c>%USERPROFILE%\EplanMcp\logs</c> and exports redacted diagnostic ZIP archives.
/// </summary>
internal sealed partial class LogsView(string logsFolder, string configFile) : INotifyPropertyChanged
{
    private const int MaxRows = 400;
    private static readonly Regex _secret = SecretPattern();

    private List<LogRow> _all = [];
    private string _levelFilter = "ALL";
    private string _search = string.Empty;
    private HashSet<LogRow> _cleared = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LogsFolder => logsFolder;

    public IReadOnlyList<LogRow> Visible { get; private set; } = [];

    public int TotalCount => _all.Count;

    public int WarnCount => _all.Count(r => r.Level == "WARN");

    public int ErrorCount => _all.Count(r => r.Level == "ERROR");

    public void SetFilter(string level, string search)
    {
        _levelFilter = level;
        _search = search.Trim();
        ApplyFilter();
    }

    /// <summary>Hides the entries shown now; the files stay as they are, and newer entries show on the next reload.</summary>
    public void ClearShown()
    {
        _cleared = [.. _all];
        ApplyFilter();
    }

    public void Reload()
    {
        var rows = new List<LogRow>();
        if (Directory.Exists(logsFolder))
        {
            IEnumerable<string> files = Directory.EnumerateFiles(logsFolder, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(6);

            foreach (string file in files)
            {
                string source = Path.GetFileName(file);
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    var lines = new List<string>();
                    while (reader.ReadLine() is { } line)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            lines.Add(line);
                        }
                    }

                    foreach (string line in lines.TakeLast(150).Reverse())
                    {
                        rows.Add(ParseLine(source, line));
                        if (rows.Count >= MaxRows)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip locked/unreadable file without blocking the log viewer.
                }

                if (rows.Count >= MaxRows)
                {
                    break;
                }
            }
        }

        _all = rows;
        ApplyFilter();
    }

    public string ExportDiagnosticsZip()
    {
        string root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(logsFolder)) ?? logsFolder;
        string exportsDir = Path.Combine(root, "exports");
        Directory.CreateDirectory(exportsDir);
        string zipPath = Path.Combine(exportsDir, string.Create(CultureInfo.InvariantCulture, $"eplan-mcp-diag-{DateTime.Now:yyyyMMdd-HHmmss}.zip"));
        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        if (Directory.Exists(logsFolder))
        {
            foreach (string file in Directory.EnumerateFiles(logsFolder, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(6))
            {
                AddRedactedEntry(zip, "logs/" + Path.GetFileName(file), file);
            }
        }

        if (File.Exists(configFile))
        {
            AddRedactedEntry(zip, "config.json", configFile);
        }

        return zipPath;
    }

    private static void AddRedactedEntry(ZipArchive zip, string entryName, string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string text = _secret.Replace(reader.ReadToEnd(), "<redacted>");
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Continue archiving other diagnostic files.
        }
    }

    /// <summary>
    /// One line of a log as a row: the Workbench writes "2026-11-20T14:18:13.154Z Information …", the Bridge
    /// "2026-11-20 03:55:14.225 [Information] …". The source is the file's program ("Workbench", "Bridge"); the message
    /// is what follows the level. A line in neither form keeps its text, and only a level word at its start counts.
    /// </summary>
    internal static LogRow ParseLine(string file, string raw)
    {
        string clean = ProjectText.Line(raw);
        string source = Program(file);
        Match match = LinePattern().Match(clean);
        if (!match.Success)
        {
            return new LogRow("--:--:--", "INFO", source, clean);
        }

        string level = match.Groups["level"].Value.ToUpperInvariant() switch
        {
            "ERROR" or "CRITICAL" or "FATAL" or "ERR" => "ERROR",
            "WARNING" or "WARN" => "WARN",
            _ => "INFO",
        };
        return new LogRow(match.Groups["time"].Value, level, source, match.Groups["message"].Value);
    }

    /// <summary>"workbench-20261120.log" is the Workbench's, "bridge-startup-error.log" the Bridge's.</summary>
    private static string Program(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        string first = name.Split('-')[0];
        return first.Length == 0 ? name : char.ToUpperInvariant(first[0]) + first[1..];
    }

    private void ApplyFilter()
    {
        IEnumerable<LogRow> query = _all.Where(r => !_cleared.Contains(r));
        if (_levelFilter != "ALL")
        {
            query = query.Where(r => r.Level == _levelFilter);
        }

        if (_search.Length > 0)
        {
            query = query.Where(r => r.Message.Contains(_search, StringComparison.OrdinalIgnoreCase) || r.Source.Contains(_search, StringComparison.OrdinalIgnoreCase));
        }

        Visible = [.. query];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    [GeneratedRegex(@"[A-Za-z0-9_-]{43,}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}[T ](?<time>\d{2}:\d{2}:\d{2})\S*\s+\[?(?<level>[A-Za-z]+)\]?\s+(?<message>.*)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LinePattern();
}
