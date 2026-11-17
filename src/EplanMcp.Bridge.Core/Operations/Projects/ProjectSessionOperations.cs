using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The checked argument of eplan_project_open: a local <c>.elk</c> file that exists.</summary>
internal sealed class ProjectOpenRequest
{
    private ProjectOpenRequest(string linkFile, string path, bool alreadyOpen)
    {
        LinkFile = linkFile;
        Path = path;
        AlreadyOpen = alreadyOpen;
    }

    public string LinkFile { get; }

    /// <summary>The path without the extension, as the catalog reports it.</summary>
    public string Path { get; }

    public bool AlreadyOpen { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public string StateFingerprint() => Fingerprint.Of([LinkFile, AlreadyOpen ? "open" : "closed"]);

    /// <summary>
    /// Checks the arguments. Planning runs before approval and the path may be outside every scratch root, so it only
    /// checks the text (MCP server standard WS-29); <paramref name="readFiles"/> adds the existence check after approval.
    /// </summary>
    public static ProjectOpenRequest Check(ProjectOpenArgs args, IProjectCatalog catalog, bool readFiles)
    {
        string given = args.Path ?? string.Empty;
        bool local = given.Length > 2 && char.IsLetter(given[0]) && given[1] == ':' && (given[2] is '\\' or '/') && !given.Any(char.IsControl)
            && System.IO.Path.GetInvalidPathChars().All(c => given.IndexOf(c) < 0);
        // eplan_project_list and eplan_project_close report the path without the extension: take the .elk beside it.
        if (local && !IsElk(given))
        { given += ".elk"; }
        if (!local || IsDeviceName(System.IO.Path.GetFileNameWithoutExtension(given)))
        { throw WriteArguments.Invalid(["'path' must be the full local path of a project's .elk file."]); }
        string linkFile = System.IO.Path.GetFullPath(given);
        if (readFiles && !File.Exists(linkFile))
        { throw WriteArguments.Invalid(["'path' names no file. eplan_project_create returns the path of a new project."]); }
        string path = System.IO.Path.ChangeExtension(linkFile, null);
        bool open = catalog.ListOpenProjects().Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        return new ProjectOpenRequest(linkFile, path, open);
    }

    private static bool IsDeviceName(string name) => name.Length is 3 or 4 && (name.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL"
        || (name.Length == 4 && name[3] is >= '1' and <= '9' && name.Substring(0, 3).ToUpperInvariant() is "COM" or "LPT"));

    private static bool IsElk(string path) => string.Equals(System.IO.Path.GetExtension(path), ".elk", StringComparison.OrdinalIgnoreCase);
}

/// <summary><see cref="OperationNames.ProjectOpenPlan"/>: one <c>open</c> unless the project is open already. Reads only.</summary>
public sealed class ProjectOpenPlanOperation(IProjectCatalog catalog) : Operation<ProjectOpenArgs, WritePlan>
{
    public override string Name => OperationNames.ProjectOpenPlan;

    protected override WritePlan Execute(ProjectOpenArgs args)
    {
        ProjectOpenRequest request = ProjectOpenRequest.Check(args, catalog, readFiles: false);
        return new WritePlan
        {
            Project = request.Name,
            ProjectPath = request.Path,
            Changes = request.AlreadyOpen ? [] : [new PlannedChange { Action = "open", Kind = "project", Name = request.Name }],
            Fingerprint = request.StateFingerprint(),
        };
    }
}

/// <summary><see cref="OperationNames.ProjectOpenExecute"/>: opens the project and checks EPLAN lists it. Changes no project data, so there is no transaction.</summary>
public sealed class ProjectOpenExecuteOperation(IProjectCatalog catalog, IProjectSessions sessions) : Operation<ProjectOpenArgs, ProjectSessionData>, IWriteOperation
{
    public override string Name => OperationNames.ProjectOpenExecute;

    protected override ProjectSessionData Execute(ProjectOpenArgs args)
    {
        ProjectOpenRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one project.open.plan returned."); }
            request = ProjectOpenRequest.Check(args, catalog, readFiles: true);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The project was opened or closed after the plan was made. Call eplan_project_open again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Name });
            }
            sessions.Open(request.LinkFile);
            OpenProject opened = catalog.ListOpenProjects().FirstOrDefault(p => string.Equals(p.Path, request.Path, StringComparison.OrdinalIgnoreCase))
                ?? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not open the project, and no dialog was shown. Check eplan_message_list.", retryable: false);
            return new ProjectSessionData { Project = opened.Name, Path = opened.Path, Current = opened.Current };
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }
    }
}

/// <summary><see cref="OperationNames.ProjectClosePlan"/>: one <c>close</c>. Reads only.</summary>
public sealed class ProjectClosePlanOperation(IProjectCatalog catalog) : Operation<ProjectCloseArgs, WritePlan>
{
    public override string Name => OperationNames.ProjectClosePlan;

    protected override WritePlan Execute(ProjectCloseArgs args)
    {
        OpenProject project = Resolve(args, catalog);
        return new WritePlan
        {
            Project = project.Name,
            ProjectPath = project.Path,
            Changes = [new PlannedChange { Action = "close", Kind = "project", Name = project.Name }],
            Fingerprint = Fingerprint.Of([project.Path]),
        };
    }

    internal static OpenProject Resolve(ProjectCloseArgs args, IProjectCatalog catalog)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        return violations.Count > 0 ? throw WriteArguments.Invalid(violations) : ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
    }
}

/// <summary><see cref="OperationNames.ProjectCloseExecute"/>: closes the project and checks EPLAN no longer lists it. EPLAN keeps every committed change on disk, so there is nothing to save.</summary>
public sealed class ProjectCloseExecuteOperation(IProjectCatalog catalog, IProjectSessions sessions) : Operation<ProjectCloseArgs, ProjectSessionData>, IWriteOperation
{
    public override string Name => OperationNames.ProjectCloseExecute;

    protected override ProjectSessionData Execute(ProjectCloseArgs args)
    {
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one project.close.plan returned."); }
            OpenProject project = ProjectClosePlanOperation.Resolve(args, catalog);
            if (!string.Equals(Fingerprint.Of([project.Path]), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "'project' now names another project. Call eplan_project_close again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = project.Name });
            }
            sessions.Close(project.Path);
            if (catalog.ListOpenProjects().Any(p => string.Equals(p.Path, project.Path, StringComparison.OrdinalIgnoreCase)))
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN kept the project open.", retryable: false); }
            return new ProjectSessionData { Project = project.Name, Path = project.Path };
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }
    }
}
