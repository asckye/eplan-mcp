using System.Text.Json;

namespace EplanMcp.Packs;

/// <summary>
/// Which files a pack may hold (ADR-0024 section 1): per kind, the extensions allowed; EPLAN's native files as api-notes
/// A35 records them, otherwise JSON, PNG, plain text (CSV included) and Markdown, SVG without scripts, and as documents
/// XLSX, PDF and DOCX, which EplanMcp stores and hashes but never opens or parses (ADR-0024 revision of 2026-10-21).
/// Executable or script types and macro-enabled Office files are refused wherever they appear. The file names
/// <c>pack.json</c>, <c>pack.lock.json</c> and <c>pack.sig</c> are the pack's own.
/// </summary>
public static class ResourceTypes
{
    /// <summary>The detached CMS signature over the pack digest (ADR-0024 section 3); never a resource.</summary>
    public const string SignatureFileName = "pack.sig";

    private static readonly string[] _executable =
        [".dll", ".exe", ".ps1", ".psm1", ".bat", ".cmd", ".com", ".vbs", ".vbe", ".vb", ".js", ".jse", ".wsf", ".hta", ".cs", ".msi", ".lnk", ".scr", ".reg", ".jar"];

    private static readonly string[] _macroEnabled = [".xlsm", ".xltm", ".docm", ".dotm", ".pptm", ".potm"];

    private static readonly Dictionary<ResourceKind, string[]> _allowed = new()
    {
        [ResourceKind.BasicProject] = [".zw9"],
        [ResourceKind.ProjectBackup] = [".zw1"],
        [ResourceKind.Project] = [".elk", ".ell", ".elr"],
        [ResourceKind.Macro] = [".ema", ".ems", ".emp"],
        [ResourceKind.Frame] = [".fn1"],
        [ResourceKind.MaterialMapping] = [".json", ".csv"],
        [ResourceKind.Document] = [".md", ".txt", ".xlsx", ".pdf", ".docx"],
        [ResourceKind.Rule] = [".json", ".md", ".txt"],
        [ResourceKind.Preview] = [".png", ".svg"],
        [ResourceKind.Other] = [".fc2", ".epj", ".zw6", ".zw7"],
    };

    /// <summary>True for <c>pack.json</c>, <c>pack.lock.json</c> and <c>pack.sig</c>.</summary>
    public static bool IsPackFile(string path) => path is PackManifest.FileName or PackManifest.LockFileName or SignatureFileName;

    /// <summary>Executable or script types, which no pack holds under any name or kind.</summary>
    public static bool IsExecutable(string path) => _executable.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why no pack holds a file at <paramref name="path"/>, under any name or kind, or null: executable and script types,
    /// and Office files that can hold macros.
    /// </summary>
    public static string? Forbidden(string path) =>
        IsExecutable(path) ? "is an executable or script type, which packs never hold"
        : _macroEnabled.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)) ? "is a macro-enabled Office file, which packs never hold; save it without macros (.xlsx, .docx)"
        : null;

    /// <summary>What is wrong with a resource of <paramref name="kind"/> at <paramref name="path"/>, or null when its type is allowed.</summary>
    public static string? Problem(ResourceKind kind, string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (Forbidden(path) is { } forbidden)
        {
            return forbidden;
        }

        bool allowed = kind switch
        {
            ResourceKind.Form => extension.Length == 4 && extension[1] == 'f' && char.IsAsciiDigit(extension[2]) && char.IsAsciiDigit(extension[3]),
            ResourceKind.Module or ResourceKind.ConfigSchema or ResourceKind.ConfigLayer or ResourceKind.Gap or ResourceKind.FactSet
                or ResourceKind.Workflow or ResourceKind.Sample => extension == ".json",
            _ => _allowed.TryGetValue(kind, out string[]? extensions) && extensions.Contains(extension),
        };
        return allowed ? null : $"has an extension a {JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString())} resource may not have (ADR-0024)";
    }
}
