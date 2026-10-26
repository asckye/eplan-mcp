using System.Text;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Exports, remaps, imports without a transaction and verifies counts by exporting again (api-notes A47).</summary>
public sealed class TranslationImportExecuteOperation(ITranslationDatabaseWriter writer) : Operation<TranslationImportArgs, TranslationImportData>, IWriteOperation
{
    /// <summary>The Host attaches the backup archive as metadata.backup and suspends further writes (ADR-0019).</summary>
    public const string UnknownMessage =
        "The translation database may be partly changed, and an import cannot be rolled back. Do not call again. Ask the engineer to check the translation database in EPLAN and, if needed, restore the backup made before the import (metadata.backup).";

    public override string Name => OperationNames.TranslationImportExecute;

    protected override TranslationImportData Execute(TranslationImportArgs args)
    {
        bool importing = false;
        string folder = Path.Combine(Path.GetTempPath(), "eplanmcp-translation-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint) || string.IsNullOrEmpty(args.Database))
            {
                throw InvalidArgument("'fingerprint' and 'database' from translation.import.plan are required.");
            }

            (ImportSource source, TranslationTable table, ConfiguredDatabase database) = TranslationImportPlanOperation.Check(args, writer);
            if (!string.Equals(source.Sha256, args.Fingerprint, StringComparison.Ordinal)
                || !string.Equals(database.Database, args.Database, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale,
                    "The file or configured translation database changed after planning. Call the tool again for a new plan; nothing was imported.");
            }

            Directory.CreateDirectory(folder);
            try
            {
                TranslationTable before = Export(Path.Combine(folder, "before.txt"), table.ReferenceLanguage);
                string file = Path.Combine(folder, "import.txt");
                File.WriteAllText(file, table.Remap(before), Encoding.Unicode);
                OperationContext.Current.ThrowIfCancellationRequested();
                importing = true;
                if (!writer.Import(file, table.ReferenceLanguage))
                {
                    throw new InvalidOperationException("ImportDatabaseTexts returned false.");
                }

                TranslationTable after = Export(Path.Combine(folder, "after.txt"), table.ReferenceLanguage);
                (int added, int filled, int kept, bool missing) = table.Compare(before, after);
                if (missing)
                {
                    throw new InvalidOperationException("The export after importing is missing an input entry.");
                }

                return new TranslationImportData
                {
                    Database = database.Database,
                    Entries = table.Rows.Count,
                    ReferenceLanguage = table.ReferenceLanguage,
                    Added = added,
                    Filled = filled,
                    Kept = kept,
                };
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
                importing ? UnknownMessage : "The translation import could not be prepared; nothing was changed. Ask the engineer to check the file and translation database.", ex);
            error.Error.Execution = importing ? ExecutionState.Unknown : ExecutionState.NotStarted;
            throw error;
        }
    }

    private TranslationTable Export(string file, string referenceLanguage)
    {
        if (!writer.Export(file))
        {
            throw new InvalidOperationException("ExportDatabaseTexts returned false.");
        }

        return TranslationTable.Parse(File.ReadAllBytes(file), referenceLanguage, export: true);
    }
}
