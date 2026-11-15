using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Imports a checked file through the shared translation write pipeline (api-notes A47).</summary>
public sealed class TranslationImportExecuteOperation(ITranslationDatabaseWriter writer) : Operation<TranslationImportArgs, TranslationImportData>, IWriteOperation
{
    /// <summary>The Host attaches the backup archive as metadata.backup and suspends further writes (ADR-0019).</summary>
    public const string UnknownMessage = TranslationWrite.UnknownMessage;

    public override string Name => OperationNames.TranslationImportExecute;

    protected override TranslationImportData Execute(TranslationImportArgs args)
    {
        if (string.IsNullOrEmpty(args.Fingerprint) || string.IsNullOrEmpty(args.Database))
        {
            throw WriteArguments.NotStarted(InvalidArgument("'fingerprint' and 'database' from translation.import.plan are required."));
        }

        (ImportSource source, TranslationTable table, ConfiguredDatabase database) = TranslationImportPlanOperation.Check(args, writer);
        if (!string.Equals(source.Sha256, args.Fingerprint, StringComparison.Ordinal)
            || !string.Equals(database.Database, args.Database, StringComparison.Ordinal))
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PlanStale,
                "The file or configured translation database changed after planning. Call the tool again for a new plan; nothing was imported."));
        }

        (int added, int filled, int kept) = TranslationWrite.Run(writer, table);
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
}
