using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of the plan or run of an export of a whole project (docs/tools/eplan_export_*.md, sections 2
/// and 4): every argument problem at once, then the project, then what the kind needs (labels: the scheme and its
/// output format).
/// </summary>
internal sealed class ProjectExportRequest
{
    /// <summary>EPLAN's own scheme of <c>USER.Labelling.Config</c>, never offered (tool spec 5).</summary>
    public const string InternalScheme = "InternalRefDefScheme";

    /// <summary>
    /// The labelling outputs S25 verified, by their converter (the scheme's <c>Data.File.Format</c>; the first Large
    /// run read these two names from EPLAN's own schemes, api-notes B9), and the file's extension: text and xlsx, which
    /// needs no Excel (A25). Others are refused until a Large test has verified them; .xls is never asked for, as it
    /// needs Excel (EPLAN XML docs).
    /// </summary>
    private static readonly Dictionary<string, string> _labelExtensions = new(StringComparer.Ordinal)
    {
        ["XmlLblTextExportConverterImpl"] = ".txt",
        ["XmlLblExcelExportConverterImpl"] = ".xlsx",
    };

    private ProjectExportRequest(OpenProject project, string? extension, string? scheme, string? language)
    {
        Project = project;
        Extension = extension;
        Scheme = scheme;
        Language = language;
    }

    public OpenProject Project { get; }

    /// <summary>The extension the Host gives the file; null for PXF, where EPLAN adds it.</summary>
    public string? Extension { get; }

    /// <summary>Labels: the scheme as EPLAN names it.</summary>
    public string? Scheme { get; }

    /// <summary>Labels: the language code EPLAN reads texts in.</summary>
    public string? Language { get; }

    public static ProjectExportRequest Check(ProjectExportKind kind, ExportArgs args, IProjectCatalog catalog, IProjectExporter exporter)
    {
        string? language = null;
        if (kind == ProjectExportKind.Labels)
        {
            var violations = new List<string>();
            if (string.IsNullOrWhiteSpace(args.Scheme))
            {
                violations.Add("'scheme' is required: the name of a labelling scheme, such as 'Device tag list'.");
            }

            language = exporter.LanguageCode(string.IsNullOrWhiteSpace(args.Language) ? null : args.Language!.Trim());
            if (language is null)
            {
                violations.Add("'language' is not a language EPLAN knows. Use a code like zh_CN or en_US, or omit it for EPLAN's user interface language.");
            }

            if (violations.Count > 0)
            {
                throw WriteArguments.Invalid(violations);
            }
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        if (kind != ProjectExportKind.Labels)
        {
            return new ProjectExportRequest(project, kind == ProjectExportKind.PartsList ? ".xml" : null, null, null);
        }

        LabelScheme scheme = FindScheme(args.Scheme!.Trim(), exporter.LabelSchemes());
        return new ProjectExportRequest(project, LabelExtension(scheme), scheme.Name, language);
    }

    /// <summary>The scheme of this name, or else the one whose name differs only in case; INVALID_ARGUMENT with the candidates otherwise.</summary>
    private static LabelScheme FindScheme(string name, IReadOnlyList<LabelScheme> all)
    {
        LabelScheme[] offered = [.. all.Where(s => !string.Equals(s.Name, InternalScheme, StringComparison.Ordinal))];
        LabelScheme[] alike = [.. offered.Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))];
        LabelScheme? found = alike.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal)) ?? (alike.Length == 1 ? alike[0] : null);
        if (found is not null)
        {
            return found;
        }

        const string Violation = "'scheme' is not a labelling scheme in EPLAN; use one of metadata.candidates.";
        throw new BridgeErrorException(
            ErrorReason.InvalidArgument,
            "Invalid arguments: " + Violation,
            retryable: false,
            new Dictionary<string, string>
            {
                ["violations"] = Violation,
                ["scheme"] = name,
                ["candidates"] = string.Join("\n", offered.Take(ProjectResolver.MaxCandidates).Select(s => s.Name)),
                ["candidateCount"] = offered.Length.ToString(CultureInfo.InvariantCulture),
            });
    }

    private static string LabelExtension(LabelScheme scheme)
    {
        if (scheme.Format is not null && _labelExtensions.TryGetValue(scheme.Format, out string? extension))
        {
            return extension;
        }

        string violation = $"'scheme' writes a format this tool does not export yet ({scheme.Format ?? "unknown"}); choose a scheme that writes text or an Excel workbook (.xlsx).";
        throw new BridgeErrorException(
            ErrorReason.InvalidArgument,
            "Invalid arguments: " + violation,
            retryable: false,
            new Dictionary<string, string> { ["violations"] = violation, ["scheme"] = scheme.Name, ["format"] = scheme.Format ?? string.Empty });
    }
}
