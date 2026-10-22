namespace EplanMcp.Contracts;

/// <summary>
/// The system master data a write changes (ADR-0019), as the plan reports it: the Host decides on it whether the write
/// is allowed, what the engineer is asked, which backup is made first and which suspension applies.
/// </summary>
public sealed class MasterDataTarget
{
    /// <summary>Which database: <see cref="MasterDataKinds.Parts"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The configured database as EPLAN's setting has it, path variables unexpanded; of a SQL Server connection string
    /// only the server and the database, never a login.
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The file database's full path, its variables expanded by EPLAN; empty for SQL Server or when EPLAN cannot expand it.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>True when the database is on SQL Server, which master-data writes never change (ADR-0019).</summary>
    public bool Server { get; set; }
}
