namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads a project's report pages and updates reports, implemented by the EPLAN adapter layer
/// (docs/tools/eplan_report_update.md, section 5). Page names are exact, as the project has them. Main thread only;
/// <see cref="UpdateAll"/> and <see cref="Update"/> only inside the write's transaction. EPLAN's failure is
/// EPLAN_OPERATION_FAILED.
/// </summary>
public interface IReportUpdater
{
    /// <summary>Every report page of the project: the pages with a report block (<c>Page.ReportBlock</c>), in EPLAN's order.</summary>
    public IReadOnlyList<ReportPageRecord> ReportPages(string projectPath);

    /// <summary>Updates all reports of the project, which may create, replace and remove report pages (api-notes A16).</summary>
    public void UpdateAll(string projectPath);

    /// <summary>
    /// Updates the report page <paramref name="page"/>. False when no page has that name any more: updating an earlier
    /// page of the same report may have removed it.
    /// </summary>
    public bool Update(string projectPath, string page);
}
