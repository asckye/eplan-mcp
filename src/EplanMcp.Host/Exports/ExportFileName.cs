using System.Buffers;
using System.Collections.Frozen;

namespace EplanMcp.Host.Exports;

/// <summary>
/// The <c>file_name</c> of the export tools (docs/tools/eplan_export_pdf.md, section 2; SEC-20): a file name only, so
/// the file can only land in the export folder. The extension is the tool's to add, or EPLAN's (PXF).
/// </summary>
internal static class ExportFileName
{
    public const int MaxLength = 100;
    public const string PdfExtension = ".pdf";
    public const string PxfExtension = ".epj";
    public const string XmlExtension = ".xml";

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
    public static string Pdf(string? fileName, string project) => WithExtension(fileName, project, PdfExtension);

    /// <summary>
    /// A file name the tool gives <paramref name="extension"/>: <paramref name="fileName"/>, or else (also when it is the
    /// extension alone) <paramref name="fallback"/> as a folder name, with the extension added unless it ends with it.
    /// </summary>
    public static string WithExtension(string? fileName, string fallback, string extension)
    {
        string name = string.IsNullOrEmpty(Stem(fileName, extension)) ? ExportFolders.FolderName(fallback) : fileName!;
        return name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension;
    }

    /// <summary>
    /// The PXF file's name without the extension, which EPLAN adds (S25): <paramref name="fileName"/> without a final
    /// <c>.epj</c>, or else the project's name. Whether EPLAN adds a second one to a name that has it is not verified
    /// (docs/tools/eplan_export_pxf.md, section 5), so the Bridge never gets one.
    /// </summary>
    public static string Pxf(string? fileName, string project) => Stem(fileName, PxfExtension) is { Length: > 0 } name ? name : ExportFolders.FolderName(project);

    /// <summary><paramref name="fileName"/> without a final <paramref name="extension"/> (any case); null stays null.</summary>
    public static string? Stem(string? fileName, string extension) =>
        fileName is not null && fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? fileName[..^extension.Length] : fileName;

    /// <summary>Whether Windows treats <paramref name="name"/> as a device: the part before the first dot, trailing spaces ignored.</summary>
    public static bool IsReserved(string name) => _reserved.Contains(name.Split('.')[0].TrimEnd(' '));
}
