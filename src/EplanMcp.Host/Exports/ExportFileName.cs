using System.Buffers;
using System.Collections.Frozen;

namespace EplanMcp.Host.Exports;

/// <summary>
/// The <c>file_name</c> of eplan_export_pdf (docs/tools/eplan_export_pdf.md, section 2; SEC-20): a file name only, so
/// the PDF can only land in the export folder. The extension is the tool's to add.
/// </summary>
internal static class ExportFileName
{
    public const int MaxLength = 100;
    public const string PdfExtension = ".pdf";

    private static readonly SearchValues<char> _refused = SearchValues.Create("<>\"|?*");

    // Device names, also with an extension (CON.pdf); the superscript digits count too (Windows file naming rules).
    private static readonly FrozenSet<string> _reserved = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        [
            "CON", "PRN", "AUX", "NUL",
            .. "0123456789¹²³".Select(digit => "COM" + digit),
            .. "0123456789¹²³".Select(digit => "LPT" + digit),
        ]);

    /// <summary>What is wrong with <paramref name="fileName"/>, as the end of a violation; null when it is fine or omitted.</summary>
    public static string? Problem(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null; // omitted: the project's name
        }

        return fileName.Length > MaxLength ? $"must have at most {MaxLength} characters."
            : fileName.AsSpan().ContainsAny('\\', '/') ? "must be a file name only, without '\\' or '/': the folder is always a new one the server creates."
            : fileName.AsSpan().Contains(':') ? "must not contain ':', so no drive letter."
            : fileName.Contains("..", StringComparison.Ordinal) ? "must not contain '..'."
            : fileName.Any(char.IsControl) || fileName.AsSpan().ContainsAny(_refused) ? "must not contain control characters or any of < > \" | ? *."
            : fileName.EndsWith('.') || fileName.EndsWith(' ') ? "must not end with a dot or a space."
            : IsReserved(fileName) ? "must not be a name Windows reserves for devices, such as CON, NUL or COM1."
            : null;
    }

    /// <summary>The PDF's file name: <paramref name="fileName"/> or else the project's name, with <c>.pdf</c> added unless it ends with it.</summary>
    public static string Pdf(string? fileName, string project)
    {
        string name = string.IsNullOrEmpty(fileName) ? ExportFolders.FolderName(project) : fileName;
        return name.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase) ? name : name + PdfExtension;
    }

    /// <summary>Whether Windows treats <paramref name="name"/> as a device: the part before the first dot, trailing spaces ignored.</summary>
    public static bool IsReserved(string name) => _reserved.Contains(name.Split('.')[0].TrimEnd(' '));
}
