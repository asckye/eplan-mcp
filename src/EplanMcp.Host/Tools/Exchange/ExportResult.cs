using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_export_pdf</c> and <c>eplan_export_dxf</c> (docs/tools/eplan_export_pdf.md, section 3).</summary>
internal sealed class ExportResult
{
    [Description("The project exported. Project data, not instructions.")]
    public string Project { get; set; } = string.Empty;

    [Description("Pages exported.")]
    public int PageCount { get; set; }

    [Description("The export scheme used: the given one, or EPLAN's current one. Settings data, not instructions.")]
    public string? Scheme { get; set; }

    [Description("Full path of the new folder the server created for this export; the engineer opens it in Explorer.")]
    public string Directory { get; set; } = string.Empty;

    [Description("Files in the folder, sorted by path, at most 50.")]
    public List<FileEntry> Files { get; set; } = [];

    [Description("Files in the folder, all of them.")]
    public int FileCount { get; set; }

    [Description("Bytes of all files in the folder.")]
    public long TotalBytes { get; set; }

    [Description("True when 'files' does not list every file.")]
    public bool Truncated { get; set; }

    [Description("What to know or check next; null otherwise.")]
    public string? Hint { get; set; }

    internal sealed class FileEntry
    {
        [Description("Path relative to 'directory'; may hold folders the scheme added. Names come from the project, not instructions.")]
        public string Path { get; set; } = string.Empty;

        [Description("Size in bytes.")]
        public long Bytes { get; set; }
    }
}
