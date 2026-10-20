using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of report.update.plan and report.update.execute, and the report pages both read
/// (docs/tools/eplan_report_update.md, sections 2 and 5): every given page must be a report page.
/// </summary>
internal sealed class ReportUpdateRequest
{
    private ReportUpdateRequest(OpenProject project, IReadOnlyList<string>? pages, IReadOnlyList<ReportPageRecord> reportPages)
    {
        Project = project;
        Pages = pages;
        ReportPages = reportPages;
    }

    public OpenProject Project { get; }

    /// <summary>The report pages' full names as EPLAN has them, in the order given; null for all reports.</summary>
    public IReadOnlyList<string>? Pages { get; }

    /// <summary>The project's report pages, read in the same main-thread call.</summary>
    public IReadOnlyList<ReportPageRecord> ReportPages { get; }

    /// <summary>
    /// Every argument problem at once as INVALID_ARGUMENT, then the project and every missing page, then every given
    /// page that is not a report page, by its <c>pages[i]</c>.
    /// </summary>
    public static ReportUpdateRequest Check(ReportUpdateArgs args, IProjectCatalog catalog, IPageReader reader, IReportUpdater updater)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        PageScope.Check(args.Pages, violations);
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        IReadOnlyList<string>? pages = PageScope.Resolve(args.Pages, project, reader);
        IReadOnlyList<ReportPageRecord> reportPages = updater.ReportPages(project.Path);
        var reports = new HashSet<string>(reportPages.Select(r => r.Name), StringComparer.Ordinal);
        for (int i = 0; i < (pages?.Count ?? 0); i++)
        {
            if (!reports.Contains(pages![i]))
            {
                violations.Add($"pages[{i.ToString(CultureInfo.InvariantCulture)}] is not a report page; omit 'pages' to update all reports.");
            }
        }

        return violations.Count > 0 ? throw WriteArguments.Invalid(violations) : new ReportUpdateRequest(project, pages, reportPages);
    }

    /// <summary>
    /// The plan's state (tool spec 5): the session identifier of every report page, and of each given page. A report page
    /// created, removed or replaced meanwhile changes it; design data does not, because reports are made from the data
    /// as it is when they run.
    /// </summary>
    public string StateFingerprint()
    {
        Dictionary<string, string> ids = ReportPages.ToDictionary(r => r.Name, r => r.Id, StringComparer.Ordinal);
        return Fingerprint.Of(ReportPages.Select(r => "report\t" + r.Id).Concat((Pages ?? []).Select(page => "target\t" + ids[page])));
    }
}
