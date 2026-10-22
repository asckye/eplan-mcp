namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The parts database EPLAN is configured with: its setting, and for a file database the path EPLAN expands it to.</summary>
public sealed class ConfiguredDatabase
{
    /// <summary>The setting as EPLAN has it: a file with path variables, or a SQL Server connection string.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The file database's full path, its variables expanded; empty when EPLAN cannot expand it or for SQL Server.</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>The configured parts database's parts: their count, each variant counted, and their distinct part numbers.</summary>
public sealed class PartsDatabaseState
{
    public int PartCount { get; set; }

    public HashSet<string> PartNumbers { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Writes the parts database EPLAN is configured with (ADR-0019, docs/tools/eplan_part_import.md), implemented by the
/// EPLAN adapter layer: the only place that calls the parts database's writing members. Main thread only. Never selects,
/// creates or upgrades a database and never changes a setting. EPLAN's failures to open or read the database throw
/// EPLAN_OPERATION_FAILED; a failed import throws whatever EPLAN threw.
/// </summary>
public interface IPartsDatabaseWriter
{
    /// <summary>The configured database; nothing is opened.</summary>
    public ConfiguredDatabase Configured();

    /// <summary>Opens the configured database, reads its parts and closes it again.</summary>
    public PartsDatabaseState Read();

    /// <summary>
    /// Opens the configured database, imports the parts XML file <paramref name="file"/> with the tool's
    /// <paramref name="mode"/> and <paramref name="matchBy"/>, outside any transaction (S24 r11), and closes it again.
    /// </summary>
    public void Import(string file, string mode, string matchBy);

    /// <summary>
    /// Lets EPLAN synchronize the parts of the open project at <paramref name="projectPath"/> into the configured
    /// database with the tool's <paramref name="mode"/> (<c>Synchronize.Parts</c>); it stores the parts the project
    /// references in the project too (S24 r15). Inside the caller's transaction and the dispatcher's LockingStep.
    /// </summary>
    public void Synchronize(string projectPath, string mode);
}
