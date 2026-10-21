using System.Globalization;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Audit;

/// <summary>
/// A record as a row of the audit list (CTL-05, CTL-06): its texts in the current language, every value from the log
/// made plain first (CTL-12, APR-08).
/// </summary>
internal sealed class AuditItem(AuditRecord record, DateTimeOffset? localTime) : IAccessibleItem
{
    public AuditRecord Record { get; } = record;

    public string Time => localTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;

    public string Kind => Record.Kind is { } kind ? AuditView.Label("Audit.Kind.", kind) : string.Empty;

    public string Tool => ProjectText.Line(Record.Tool ?? string.Empty);

    public string Project => ProjectText.Line(Record.Project ?? string.Empty);

    /// <summary>The decision of an approval or release, the result of a write; empty for the other records.</summary>
    public string Result => (Record.Decision, Record.Execution) switch
    {
        ({ } decision, _) => AuditView.Label("Audit.Decision.", decision),
        (_, { } execution) => AuditView.Label("Audit.Execution.", execution),
        _ => string.Empty,
    };

    public string AccessibleName => Loc.Current.T("Audit.RowName", Time, Kind, Tool, Project, Result);
}
