using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The checked arguments of eplan_project_rename: an open project and a free new name in its folder.</summary>
internal sealed class ProjectRenameRequest
{
    private static readonly string[] _deviceNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    private ProjectRenameRequest(OpenProject project, string newName, string newPath)
    {
        Project = project;
        NewName = newName;
        NewPath = newPath;
    }

    public OpenProject Project { get; }

    public string NewName { get; }

    /// <summary>The new project's path without the extension, in the old project's folder.</summary>
    public string NewPath { get; }

    public string LinkFile => Project.Path + ".elk";

    public string NewLinkFile => NewPath + ".elk";

    public string StateFingerprint() => Fingerprint.Of([Project.Path, NewPath]);

    public static ProjectRenameRequest Check(ProjectRenameArgs args, IProjectCatalog catalog)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string name = args.NewName ?? string.Empty;
        if (name.Length is 0 or > 60 || name != name.Trim() || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOf('.') >= 0
            || name.Any(char.IsControl) || _deviceNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            violations.Add("'new_name' must be the project's new name: 1-60 characters, no path, no dot, no control character, no leading or trailing space, not a Windows device name such as CON.");
        }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string newPath = Path.Combine(Path.GetDirectoryName(project.Path) ?? string.Empty, name);
        if (string.Equals(newPath, project.Path, StringComparison.OrdinalIgnoreCase))
        { throw WriteArguments.Invalid(["'new_name' is the project's current name."]); }
        if (File.Exists(newPath + ".elk") || Directory.Exists(newPath + ".edb"))
        { throw WriteArguments.Invalid([$"A project named '{name}' already exists in this folder; choose another name."]); }
        return new ProjectRenameRequest(project, name, newPath);
    }
}

/// <summary><see cref="OperationNames.ProjectRenamePlan"/>: one <c>rename</c>. Reads only.</summary>
public sealed class ProjectRenamePlanOperation(IProjectCatalog catalog) : Operation<ProjectRenameArgs, WritePlan>
{
    public override string Name => OperationNames.ProjectRenamePlan;

    protected override WritePlan Execute(ProjectRenameArgs args)
    {
        ProjectRenameRequest request = ProjectRenameRequest.Check(args, catalog);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [new PlannedChange { Action = "rename", Kind = "project", Name = request.Project.Name, OldValue = request.Project.Name, NewValue = request.NewName }],
            Fingerprint = request.StateFingerprint(),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.ProjectRenameExecute"/>: the EPLAN API has no rename (docs/tools/eplan_project_rename.md), so the
/// project is backed up, copied under the new name, closed, the copy opened and the old project removed. A failed close or
/// open puts the old project back and removes the copy; old files left behind after the copy is open are a warning only.
/// </summary>
public sealed class ProjectRenameExecuteOperation(IProjectCatalog catalog, IProjectSessions sessions, IProjectBackup backup, IPageReader pages)
    : Operation<ProjectRenameArgs, ProjectRenameData>, IWriteOperation
{
    public override string Name => OperationNames.ProjectRenameExecute;

    protected override ProjectRenameData Execute(ProjectRenameArgs args)
    {
        ProjectRenameRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one project.rename.plan returned."); }
            request = ProjectRenameRequest.Check(args, catalog);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The project or the folder changed after the plan was made, so nothing was renamed. Call eplan_project_rename again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var data = new ProjectRenameData { PreviousPath = request.Project.Path };
        data.Backup = ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        try
        {
            sessions.Copy(request.LinkFile, request.NewLinkFile);
            if (!File.Exists(request.NewLinkFile))
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN made no copy under the new name, so nothing was renamed.", retryable: false); }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        OpenProject? opened = null;
        try
        {
            sessions.Close(request.Project.Path);
            sessions.Open(request.NewLinkFile);
            opened = catalog.ListOpenProjects().FirstOrDefault(p => string.Equals(p.Path, request.NewPath, StringComparison.OrdinalIgnoreCase));
        }
        catch (BridgeErrorException)
        {
            // The old project comes back below.
        }
        if (opened is null)
        {
            PutBack(request);
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "EPLAN did not open the project under its new name; the old project is open again and the copy was removed.", retryable: false));
        }

        try
        {
            sessions.Remove(request.LinkFile);
        }
        catch (BridgeErrorException)
        {
            // Read back below: the renamed project is open and usable either way.
        }
        if (File.Exists(request.LinkFile) || Directory.Exists(request.Project.Path + ".edb"))
        { data.Warning = $"The project was renamed, but EPLAN left files of the old project at '{request.Project.Path}'; delete them by hand when no longer needed."; }
        data.Project = opened.Name;
        data.Path = opened.Path;
        data.Current = opened.Current;
        return data;
    }

    private void PutBack(ProjectRenameRequest request)
    {
        try
        {
            if (catalog.ListOpenProjects().Any(p => string.Equals(p.Path, request.NewPath, StringComparison.OrdinalIgnoreCase)))
            { sessions.Close(request.NewPath); }
            if (!catalog.ListOpenProjects().Any(p => string.Equals(p.Path, request.Project.Path, StringComparison.OrdinalIgnoreCase)))
            { sessions.Open(request.LinkFile); }
            sessions.Remove(request.NewLinkFile);
        }
        catch (BridgeErrorException)
        {
            // Best effort: the error message tells the model what to check.
        }
    }
}
