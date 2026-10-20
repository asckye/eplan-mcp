using System.Text.RegularExpressions;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// Export schemes, and exports that write files the way S10 and S11 found EPLAN writes them (api-notes A14, A16): a PDF
/// is the one file named; DXF writes one file per page below the scheme's subfolders <c>&lt;project&gt;\&lt;structure&gt;\</c>;
/// an image is the page's name with <c>/</c> as <c>_</c>. <see cref="RememberScheme"/> stands for a named scheme
/// becoming the last used one, which S11 did not test. Records every export in <see cref="Exports"/>.
/// </summary>
internal sealed class FakeExporter : IExporter
{
    public Dictionary<ExportKind, List<string>> SchemeNames { get; } = new()
    {
        [ExportKind.Pdf] = ["EPLAN_default_value", "PDF_A3"],
        [ExportKind.Dxf] = ["Standard Settings", "DWG_2018"],
    };

    public Dictionary<ExportKind, string> LastUsed { get; } = new()
    {
        [ExportKind.Pdf] = "EPLAN_default_value",
        [ExportKind.Dxf] = "Standard Settings",
    };

    /// <summary>When true, an export with a named scheme makes it the last used one.</summary>
    public bool RememberScheme { get; set; }

    /// <summary>When true, setting the last used scheme back is refused.</summary>
    public bool RefuseSetLastUsed { get; set; }

    /// <summary>Thrown by every export, after it wrote its files.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Called before each image with the number of images exported so far.</summary>
    public Action<int>? BeforeImage { get; set; }

    /// <summary>The image's file name for a page; default: the page's name with '/' as '_' (A14).</summary>
    public Func<string, string>? ImageName { get; set; }

    public List<string> Exports { get; } = [];

    public bool SchemeExists(ExportKind kind, string scheme) => SchemeNames[kind].Contains(scheme, StringComparer.Ordinal);

    public IReadOnlyList<string> Schemes(ExportKind kind) => SchemeNames[kind];

    public string LastUsedScheme(ExportKind kind) => LastUsed[kind];

    public bool TrySetLastUsedScheme(ExportKind kind, string scheme)
    {
        if (RefuseSetLastUsed)
        {
            return false;
        }

        LastUsed[kind] = scheme;
        return true;
    }

    public void ExportPdf(string projectPath, IReadOnlyList<string> pages, string scheme, ExportColor color, string file)
    {
        Exports.Add($"pdf {string.Join(",", pages)} '{scheme}' {color} {Path.GetFileName(file)}");
        Remember(ExportKind.Pdf, scheme);
        Write(file);
    }

    public void ExportDxf(string projectPath, IReadOnlyList<string> pages, string scheme, string directory)
    {
        Exports.Add($"dxf {string.Join(",", pages)} '{scheme}'");
        Remember(ExportKind.Dxf, scheme);
        foreach (string page in pages)
        {
            // =GAA+A1&EFS1/1 -> <project>\=GAA\+A1\&EFS1\=GAA+A1&EFS1_1.dxf
            string[] parts = [.. Regex.Matches(page[..page.LastIndexOf('/')], "[=+&][^=+&]*").Select(m => m.Value)];
            Write(Path.Combine([directory, Path.GetFileName(projectPath), .. parts, page.Replace('/', '_') + ".dxf"]));
        }
    }

    public void ExportImage(string projectPath, string page, string directory, ImageFormat format, int widthPx)
    {
        BeforeImage?.Invoke(Exports.Count(e => e.StartsWith("image", StringComparison.Ordinal)));
        Exports.Add($"image {page} {format} {widthPx}");
        string name = ImageName?.Invoke(page) ?? page.Replace('/', '_');
        Write(Path.Combine(directory, name + (format == ImageFormat.Png ? ".png" : ".jpg")));
    }

    private void Remember(ExportKind kind, string scheme)
    {
        if (RememberScheme && scheme.Length > 0)
        {
            LastUsed[kind] = scheme;
        }
    }

    private void Write(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
