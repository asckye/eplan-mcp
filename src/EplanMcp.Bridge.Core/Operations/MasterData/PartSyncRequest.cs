using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of part.sync.plan and part.sync.execute (docs/tools/eplan_part_sync.md, sections 2 and 5): the
/// project, the parts it stores and references, the configured parts database (never one on SQL Server), and what the
/// mode does. Only parts the project stores have data to give: those the database lacks are added (append modes), those
/// it has are overwritten (overwrite modes). Parts the project only references are stored in the project from the
/// database (S24 r15), when the database has them. Both operations build it the same way and compute the fingerprint here.
/// </summary>
internal sealed class PartSyncRequest
{
    public static readonly string[] Modes = ["append_new", "overwrite_existing", "overwrite_and_append"];

    private PartSyncRequest(OpenProject project, string mode, ConfiguredDatabase database, PartsDatabaseState state, ProjectParts parts)
    {
        Project = project;
        Mode = mode;
        Database = database;
        PartCount = state.PartCount;
        ProjectPartCount = parts.Articles.Count;
        Stored = [.. parts.Articles.Select(a => a.PartNumber).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal)];
        var stored = new HashSet<string>(Stored, StringComparer.Ordinal);
        Referenced = [.. parts.References.Select(r => r.PartNumber).Where(n => n.Length > 0 && !stored.Contains(n)).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal)];
        Added = [.. Stored.Where(n => mode != "overwrite_existing" && !state.PartNumbers.Contains(n))];
        Overwritten = [.. Stored.Where(n => mode != "append_new" && state.PartNumbers.Contains(n))];
        StoredInProject = [.. Referenced.Where(state.PartNumbers.Contains)];
        Known = [.. Stored.Concat(Referenced).Where(state.PartNumbers.Contains)];
        AddedVariants = parts.Articles.Count(a => Added.Contains(a.PartNumber, StringComparer.Ordinal));
    }

    public OpenProject Project { get; }

    public string Mode { get; }

    public ConfiguredDatabase Database { get; }

    /// <summary>The database's parts now, each variant counted.</summary>
    public int PartCount { get; }

    /// <summary>The parts the project stores now, each variant counted.</summary>
    public int ProjectPartCount { get; }

    /// <summary>The part numbers the project stores, sorted.</summary>
    public List<string> Stored { get; }

    /// <summary>The part numbers the project references without storing them, sorted.</summary>
    public List<string> Referenced { get; }

    public List<string> Added { get; }

    public List<string> Overwritten { get; }

    public List<string> StoredInProject { get; }

    /// <summary>The project's part numbers the database has now, which the fingerprint holds.</summary>
    public List<string> Known { get; }

    /// <summary>The variants the project stores of the added parts: how many parts the database should gain.</summary>
    public int AddedVariants { get; }

    /// <summary>Every argument problem at once, then the project, the database (POLICY_DENIED on SQL Server) and the parts.</summary>
    public static PartSyncRequest Check(PartSyncArgs args, IProjectCatalog catalog, IPartReader reader, IPartsDatabaseWriter writer)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string mode = string.IsNullOrWhiteSpace(args.Mode) ? Modes[0] : args.Mode!.Trim();
        if (!Modes.Contains(mode, StringComparer.Ordinal))
        {
            violations.Add("'mode' must be " + string.Join(", ", Modes) + ".");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        ConfiguredDatabase database = writer.Configured();
        if (MasterDatabases.IsServer(database.Database))
        {
            throw MasterDatabases.Denied();
        }

        if (database.Database.Length == 0)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN has no parts database configured, so nothing was synchronized. Ask the engineer to choose one in EPLAN's parts management.");
        }

        return new PartSyncRequest(project, mode, database, writer.Read(), reader.ReadParts(project.Path, null));
    }

    /// <summary>The database as the plan reports it (ADR-0019).</summary>
    public MasterDataTarget Target() => new() { Kind = MasterDataKinds.Parts, Database = Database.Database, Path = Database.Path };

    /// <summary>
    /// The plan's state (tool spec 5): the database, the parts the project stores and references, and which of them the
    /// database has. Other parts of the database may change meanwhile without invalidating the plan.
    /// </summary>
    public string StateFingerprint() => Fingerprint.Of(
    [
        "database\t" + Database.Database,
        "projectParts\t" + PartImportRequest.Number(ProjectPartCount),
        .. Stored.Select(number => "stored\t" + number),
        .. Referenced.Select(number => "referenced\t" + number),
        .. Known.Select(number => "known\t" + number),
    ]);
}
