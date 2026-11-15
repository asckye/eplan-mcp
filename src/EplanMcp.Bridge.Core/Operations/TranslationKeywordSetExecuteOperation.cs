using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Writes the planned keywords through the same merging import as a file import, so an existing translation is never
/// overwritten and the counts come from exports before and after, not from EPLAN's return value (api-notes A47).
/// </summary>
public sealed class TranslationKeywordSetExecuteOperation(ITranslationDatabaseWriter writer) : Operation<TranslationKeywordSetArgs, TranslationKeywordSetData>, IWriteOperation
{
    /// <summary>The Host attaches the backup archive as metadata.backup and suspends further writes (ADR-0019).</summary>
    public const string UnknownMessage = TranslationWrite.UnknownMessage;

    public override string Name => OperationNames.TranslationKeywordSetExecute;

    protected override TranslationKeywordSetData Execute(TranslationKeywordSetArgs args)
    {
        if (string.IsNullOrEmpty(args.Fingerprint) || string.IsNullOrEmpty(args.Database))
        {
            throw WriteArguments.NotStarted(InvalidArgument("'fingerprint' and 'database' from translation.keyword.set.plan are required."));
        }

        (TranslationTable table, ConfiguredDatabase database) = TranslationKeywordSetPlanOperation.Check(args, writer);
        if (!string.Equals(TranslationKeywordSetPlanOperation.Fingerprint(table), args.Fingerprint, StringComparison.Ordinal)
            || !string.Equals(database.Database, args.Database, StringComparison.Ordinal))
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PlanStale,
                "The keywords or the configured translation database changed after planning. Call the tool again for a new plan; nothing was written."));
        }

        (int added, int filled, int kept) = TranslationWrite.Run(writer, table);
        return new TranslationKeywordSetData
        {
            Database = database.Database,
            ReferenceLanguage = table.ReferenceLanguage,
            Keywords = table.Rows.Count,
            Added = added,
            Filled = filled,
            Kept = kept,
        };
    }
}
