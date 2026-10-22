namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Which system database an export reads (docs/tools/eplan_part_export.md).</summary>
public enum MasterDataExportKind
{
    /// <summary>The parts database EPLAN is configured with, as XML or EDZ.</summary>
    Parts,
}

/// <summary>The file a database export writes, as the tools' <c>format</c> names it.</summary>
public enum MasterDataFormat
{
    Xml,
    Edz,
}

/// <summary>What <see cref="IMasterDataExporter.ExportParts"/> found in the parts database.</summary>
public sealed class PartsExport
{
    /// <summary>The configured database as EPLAN's setting has it: a file with path variables, or a SQL Server connection string.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The part numbers found, a part with several variants counted once; all of them when none were given.</summary>
    public int PartCount { get; set; }

    /// <summary>The parts found with each variant counted, as <c>MDPartsDatabase.Parts</c> lists them.</summary>
    public int VariantCount { get; set; }

    /// <summary>The part numbers given that the database does not have, in the order given; nothing was exported when there are any.</summary>
    public List<string> Missing { get; set; } = [];
}

/// <summary>
/// Exports the system databases EPLAN is configured with, implemented by the EPLAN adapter layer (api-notes A27, A30).
/// Main thread only. Reads the databases and writes only the file given, a full path in the folder the Host created;
/// never creates, upgrades or selects a database or changes a setting. EPLAN's failures throw EPLAN_OPERATION_FAILED.
/// </summary>
public interface IMasterDataExporter
{
    /// <summary>
    /// Opens the configured parts database, finds every variant of each of <paramref name="partNumbers"/> (every part
    /// when null) and, when <paramref name="file"/> is given and none is missing, exports them to it in
    /// <paramref name="format"/>. Closes the database before returning, after an error too. Without a file it only checks.
    /// </summary>
    public PartsExport ExportParts(IReadOnlyList<string>? partNumbers, MasterDataFormat format, string? file);
}
