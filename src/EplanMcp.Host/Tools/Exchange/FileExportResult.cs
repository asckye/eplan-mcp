using System.ComponentModel;
using System.Text.Json.Serialization;
using EplanMcp.Host.Exports;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The folder and its files, as every export of a whole project returns them (docs/tools/eplan_export_pxf.md, section
/// 3): the fields of eplan_export_pdf after the tool's own, in the same order.
/// </summary>
internal abstract class FileExportResult
{
    [JsonPropertyOrder(10)]
    [Description("Full path of the new folder the server created for this export; the engineer opens it in Explorer.")]
    public string Directory { get; set; } = string.Empty;

    [JsonPropertyOrder(11)]
    [Description("Files in the folder, sorted by path.")]
    public List<ExportResult.FileEntry> Files { get; set; } = [];

    [JsonPropertyOrder(12)]
    [Description("Files in the folder, all of them.")]
    public int FileCount { get; set; }

    [JsonPropertyOrder(13)]
    [Description("Bytes of all files in the folder.")]
    public long TotalBytes { get; set; }

    [JsonPropertyOrder(14)]
    [Description("True when 'files' does not list every file.")]
    public bool Truncated { get; set; }

    [JsonPropertyOrder(15)]
    [Description("What to know or check next; null otherwise.")]
    public string? Hint { get; set; }

    /// <summary>The folder, its files (at most <see cref="ExportTools.MaxFiles"/> listed, the counts of all) and the Bridge's warning.</summary>
    public void Fill(ExportOutcome outcome)
    {
        Directory = outcome.Directory;
        Files = [.. outcome.Files.Take(ExportTools.MaxFiles).Select(f => new ExportResult.FileEntry { Path = ResultShaper.CleanText(f.Path), Bytes = f.Bytes })];
        FileCount = outcome.Files.Count;
        TotalBytes = outcome.Files.Sum(f => f.Bytes);
        Truncated = Files.Count < FileCount;
        Hint = ExportTools.Hint(ResultShaper.CleanText(outcome.Data.Warning), Truncated);
    }
}
