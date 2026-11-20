using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using EplanMcp.Workbench.Approvals;

namespace EplanMcp.Workbench.Logs;

internal sealed record LogRow(string Time, string Level, string Source, string Message);

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

    private static LogRow ParseLine(string source, string raw)
    {
        string clean = ProjectText.Line(raw);
        string level = clean.Contains("ERR", StringComparison.OrdinalIgnoreCase) || clean.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ? "ERROR"
            : clean.Contains("WARN", StringComparison.OrdinalIgnoreCase) ? "WARN" : "INFO";
        string time = clean.Length >= 19 && clean[4] == '-' && clean[10] is 'T' or ' ' ? clean.Substring(11, 8) : "--:--:--";
        return new LogRow(time, level, source, clean);
    }

    private void ApplyFilter()
    {
        IEnumerable<LogRow> query = _all;
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
}
