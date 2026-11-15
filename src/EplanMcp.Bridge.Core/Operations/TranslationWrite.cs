using System.Text;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The one way this server changes the translation database: export it, remap the wanted table onto that export's own
/// header, import without a transaction, then export again and count what actually changed, because EPLAN's return
/// value is true even when nothing was written (api-notes A47). Both the file import and the keyword write use it, so
/// they share one verified route and one outcome vocabulary.
/// </summary>
internal static class TranslationWrite
{
    /// <summary>The Host attaches the backup archive as metadata.backup and suspends further writes (ADR-0019).</summary>
    internal const string UnknownMessage =
        "The translation database may be partly changed, and an import cannot be rolled back. Do not call again. Ask the engineer to check the translation database in EPLAN and, if needed, restore the backup made before the import (metadata.backup).";

    /// <summary>The configured file database, refusing SQL Server and no database at all (SEC-12).</summary>
    internal static ConfiguredDatabase Database(ITranslationDatabaseWriter writer)
    {
        ConfiguredDatabase database = writer.Configured();
        if (MasterDatabases.IsServer(database.Database))
        {
            throw MasterDatabases.Denied("translation");
        }

        return database.Database.Length > 0 ? database : throw WriteArguments.NotStarted(new BridgeErrorException(
            ErrorReason.EplanOperationFailed,
            "EPLAN has no translation database configured. Ask the engineer to select one in EPLAN's settings."));
    }

    /// <summary>Exports the database and parses it with the export's own header, COMMENT column included.</summary>
    internal static TranslationTable Export(ITranslationDatabaseWriter writer, string file, string referenceLanguage)
    {
        if (!writer.Export(file))
        {
            throw new InvalidOperationException("ExportDatabaseTexts returned false.");
        }

        return TranslationTable.Parse(File.ReadAllBytes(file), referenceLanguage, export: true);
    }

    /// <summary>
    /// Reads the database before writing it, so the counts come from the database and not from the request. Errors
    /// before the import say nothing was changed; after it the outcome is unknown, because there is no rollback.
    /// </summary>
    internal static (int Added, int Filled, int Kept) Run(ITranslationDatabaseWriter writer, TranslationTable table)
    {
        bool importing = false;
        string folder = Path.Combine(Path.GetTempPath(), "eplanmcp-translation-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            try
            {
                TranslationTable before = Export(writer, Path.Combine(folder, "before.txt"), table.ReferenceLanguage);
                string file = Path.Combine(folder, "import.txt");
                File.WriteAllText(file, table.Remap(before), Encoding.Unicode);
                OperationContext.Current.ThrowIfCancellationRequested();
                importing = true;
                if (!writer.Import(file, table.ReferenceLanguage))
                {
                    throw new InvalidOperationException("ImportDatabaseTexts returned false.");
                }

                TranslationTable after = Export(writer, Path.Combine(folder, "after.txt"), table.ReferenceLanguage);
                (int added, int filled, int kept, bool missing) = table.Compare(before, after);
                return missing
                    ? throw new InvalidOperationException("The export after importing is missing an input entry.")
                    : (added, filled, kept);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (BridgeErrorException ex) when (!importing)
        {
            throw WriteArguments.NotStarted(ex);
        }
        catch (Exception ex)
        {
            var error = new BridgeErrorException(importing ? ErrorReason.OutcomeUnknown : ErrorReason.EplanOperationFailed,
                importing ? UnknownMessage : "The translation write could not be prepared; nothing was changed. Ask the engineer to check the translation database.", ex);
            error.Error.Execution = importing ? ExecutionState.Unknown : ExecutionState.NotStarted;
            throw error;
        }
    }
}
