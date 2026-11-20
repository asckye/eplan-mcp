using System.ComponentModel;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Audit;
using EplanMcp.Workbench.Commands;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Project;

internal sealed record ProjectActivityRow(string Time, string Tool, string Project, string Result);

internal sealed record ProjectSessionRow(string Title, string Detail, CheckStatus Status);

/// <summary>
/// Live EPLAN project &amp; session workspace model backed by <see cref="HostCommands.DoctorAsync"/> and <see cref="AuditReader"/>.
/// </summary>
internal sealed class ProjectView(HostCommands host, string auditFolder) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsLoading { get; private set; }

    public bool HasRunningSession { get; private set; }

    public string SessionSummary { get; private set; } = string.Empty;

    public IReadOnlyList<ProjectSessionRow> Sessions { get; private set; } = [];

    public IReadOnlyList<ProjectActivityRow> RecentActivity { get; private set; } = [];

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        Changed();
        try
        {
            var sessions = new List<ProjectSessionRow>();
            if (host.IsAvailable)
            {
                try
                {
                    DoctorReport report = await host.DoctorAsync(cancellationToken).ConfigureAwait(true);
                    foreach (DoctorCheck check in report.Checks.Where(c => c.Id is "eplan.running" or "eplan.install" or "addin.loaded" or "suspensions"))
                    {
                        string text = ProjectText.Line(Loc.Current.Language == AppLanguage.Chinese ? check.Zh : check.En);
                        sessions.Add(new ProjectSessionRow(check.Id, text, check.Status));
                    }

                    HasRunningSession = report.Checks.Any(c => c.Id == "eplan.running" && c.Status == CheckStatus.Ok);
                    DoctorCheck? running = report.Checks.FirstOrDefault(c => c.Id == "eplan.running");
                    SessionSummary = running is not null
                        ? ProjectText.Line(Loc.Current.Language == AppLanguage.Chinese ? running.Zh : running.En)
                        : Loc.Current["Project.NoSession"];
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    HasRunningSession = false;
                    SessionSummary = Loc.Current["Project.NoSession"];
                }
            }
            else
            {
                HasRunningSession = false;
                SessionSummary = Loc.Current["Host.Missing"];
            }

            Sessions = sessions;

            try
            {
                AuditLogContents read = AuditReader.Read(auditFolder, progress: null, cancellationToken);
                RecentActivity =
                [
                    .. read.Records.TakeLast(25).Reverse().Select(r => new ProjectActivityRow(
                        r.Time?.ToLocalTime().ToString("MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture) ?? "--",
                        ProjectText.Line(r.Tool ?? "-"),
                        ProjectText.Line(string.IsNullOrWhiteSpace(r.Project) ? "-" : r.Project),
                        ProjectText.Line(r.Execution ?? r.Decision ?? r.Kind ?? "-"))),
                ];
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                RecentActivity = [];
            }
        }
        finally
        {
            IsLoading = false;
            Changed();
        }
    }

    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
