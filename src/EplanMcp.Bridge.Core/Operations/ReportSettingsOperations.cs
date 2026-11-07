using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads and sets a project's report settings, implemented by the EPLAN adapter layer. Main thread only; EPLAN refusing is EPLAN_OPERATION_FAILED.</summary>
public interface IReportSettings
{
    public bool Read(string projectPath, string setting);

    public void Write(string projectPath, string setting, bool value);
}

/// <summary>The checked arguments of eplan_report_configure and the settings that would change.</summary>
internal sealed class ReportSettingsRequest
{
    private ReportSettingsRequest(OpenProject project, List<(string Setting, bool Before, bool After)> changing)
    {
        Project = project;
        Changing = changing;
    }

    public OpenProject Project { get; }

    public List<(string Setting, bool Before, bool After)> Changing { get; }

    public string StateFingerprint() => Fingerprint.Of(new[] { Project.Path }.Concat(Changing.Select(c => c.Setting + "=" + c.Before + ">" + c.After)));

    public static ReportSettingsRequest Check(ReportSettingsArgs args, IProjectCatalog catalog, IReportSettings settings)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        Dictionary<string, bool> given = args.Settings ?? [];
        if (given.Count == 0)
        { violations.Add("Give at least one setting."); }
        violations.AddRange(given.Keys.Where(k => !ReportSettingsArgs.Allowed.Contains(k, StringComparer.Ordinal)).Select(k => "'" + k + "' is not a setting this tool changes."));
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        return new ReportSettingsRequest(project, [.. given.Select(g => (g.Key, settings.Read(project.Path, g.Key), g.Value)).Where(c => c.Item2 != c.Value)]);
    }
}

/// <summary><see cref="OperationNames.ReportSettingsPlan"/>: one <c>update</c> per setting that would change. Reads only.</summary>
public sealed class ReportSettingsPlanOperation(IProjectCatalog catalog, IReportSettings settings) : Operation<ReportSettingsArgs, WritePlan>
{
    public override string Name => OperationNames.ReportSettingsPlan;

    protected override WritePlan Execute(ReportSettingsArgs args)
    {
        ReportSettingsRequest request = ReportSettingsRequest.Check(args, catalog, settings);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Changing.Select(c => new PlannedChange
            {
                Action = "update",
                Kind = "project",
                Name = request.Project.Name,
                Property = c.Setting,
                OldValue = c.Before ? "true" : "false",
                NewValue = c.After ? "true" : "false",
            })],
            Fingerprint = request.StateFingerprint(),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.ReportSettingsExecute"/>: checks the plan's fingerprint again, sets the values and reads them
/// back. EPLAN's rollback does not cover project settings (api-notes A28), so a wrong read-back restores them here.
/// </summary>
public sealed class ReportSettingsExecuteOperation(IProjectCatalog catalog, IReportSettings settings) : Operation<ReportSettingsArgs, ReportSettingsData>, IWriteOperation
{
    public override string Name => OperationNames.ReportSettingsExecute;

    protected override ReportSettingsData Execute(ReportSettingsArgs args)
    {
        ReportSettingsRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one report.settings.plan returned."); }
            request = ReportSettingsRequest.Check(args, catalog, settings);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The report settings changed after the plan was made, so nothing was changed. Call eplan_report_configure again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        string path = request.Project.Path;
        foreach ((string setting, _, bool after) in request.Changing)
        { settings.Write(path, setting, after); }
        string? wrong = request.Changing.Where(c => settings.Read(path, c.Setting) != c.After).Select(c => c.Setting).FirstOrDefault();
        if (wrong is not null)
        {
            foreach ((string setting, bool before, _) in request.Changing)
            { settings.Write(path, setting, before); }
            var failed = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not store a report setting as asked, so the old values were restored. metadata.setting names it.",
                retryable: false, new Dictionary<string, string> { ["setting"] = wrong });
            failed.Error.Execution = ExecutionState.RolledBack;
            throw failed;
        }
        return new ReportSettingsData { Project = request.Project.Name };
    }
}
