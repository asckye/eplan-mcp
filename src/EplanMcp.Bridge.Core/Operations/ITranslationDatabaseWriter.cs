namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Configured translation database access, on EPLAN's main thread (ADR-0019, api-notes A47).</summary>
public interface ITranslationDatabaseWriter
{
    /// <summary>The configured file database and its expanded path; rejects SQL Server.</summary>
    public ConfiguredDatabase Configured();

    /// <summary>Exports the database as UTF-16 tab-separated text to the given temporary file.</summary>
    public bool Export(string file);

    /// <summary>Imports a remapped tab file using one reference language, quietly and without a transaction.</summary>
    public bool Import(string file, string referenceLanguage);
}
