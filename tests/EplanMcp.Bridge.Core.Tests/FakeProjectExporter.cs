using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// Exports of a whole project that write files the way S25 found EPLAN writes them (api-notes A25): PXF adds
/// <c>.epj</c> to the name and leaves an empty folder of that name next to the file; the parts list and labels are the
/// file named. The labelling schemes are the ones EPLAN 2026 ships, with S25's formats, and its internal one.
/// Records every export in
/// <see cref="Exports"/>; <see cref="During"/> runs inside each export, as a cancel arriving meanwhile would.
/// </summary>
internal sealed class FakeProjectExporter : IProjectExporter
{
    /// <summary>Thrown by every export, after it wrote its files.</summary>
    public Exception? Failure { get; set; }

    public Action? During { get; set; }

    public List<string> Exports { get; } = [];

    public List<LabelScheme> Schemes { get; } =
    [
        new() { Name = "InternalRefDefScheme" },
        new() { Name = "Device tag list", Format = "XmlLblTextExportConverterImpl" },
        new() { Name = "Terminal diagram", Format = "XmlLblExcelExportConverterImpl" },
        new() { Name = "XML list", Format = "XmlLblXmlExportConverterImpl" },
        new() { Name = "Unreadable" },
    ];

    public void ExportPxf(string projectPath, string file, bool includeMasterData)
    {
        Exports.Add($"pxf {projectPath} {Path.GetFileName(file)} {includeMasterData}");
        During?.Invoke();
        File.WriteAllText(file + ".epj", "pxf");
        Directory.CreateDirectory(file);
        ThrowIfFailing();
    }

    public void ExportPartsList(string projectPath, string file)
    {
        Exports.Add($"parts {projectPath} {Path.GetFileName(file)}");
        During?.Invoke();
        File.WriteAllText(file, "<parts/>");
        ThrowIfFailing();
    }

    public IReadOnlyList<LabelScheme> LabelSchemes() => Schemes;

    /// <summary>EPLAN's user interface language is en_US here; xx_XX is one EPLAN does not know.</summary>
    public string? LanguageCode(string? language) => language is null ? "en_US" : language == "xx_XX" ? null : language;

    public void ExportLabels(string projectPath, string scheme, string language, string file)
    {
        Exports.Add($"labels {projectPath} '{scheme}' {language} {Path.GetFileName(file)}");
        During?.Invoke();
        File.WriteAllText(file, "-K1");
        ThrowIfFailing();
    }

    /// <summary>The sample project's translation languages are 20 (S25); a few stand for them.</summary>
    public List<string> Translation { get; } = ["de_DE", "en_US", "fr_FR", "zh_CN"];

    public IReadOnlyList<string> TranslationLanguages(string projectPath) => Translation;

    public void ExportUntranslated(string projectPath, IReadOnlyList<string> languages, string file)
    {
        Exports.Add($"untranslated {projectPath} {string.Join(",", languages)} {Path.GetFileName(file)}");
        During?.Invoke();
        File.WriteAllText(file, "zh_CN\ten_US\tCOMMENT\n");
        ThrowIfFailing();
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
