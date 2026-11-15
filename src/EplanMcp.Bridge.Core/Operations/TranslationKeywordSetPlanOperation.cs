using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads the configured translation database and says, per requested translation, whether the write adds a keyword,
/// fills an empty translation, or leaves an existing one alone (api-notes A47). Changes nothing.
/// </summary>
public sealed class TranslationKeywordSetPlanOperation(ITranslationDatabaseWriter writer) : Operation<TranslationKeywordSetArgs, WritePlan>
{
    public override string Name => OperationNames.TranslationKeywordSetPlan;

    protected override WritePlan Execute(TranslationKeywordSetArgs args)
    {
        (TranslationTable table, ConfiguredDatabase database) = Check(args, writer);
        string folder = Path.Combine(Path.GetTempPath(), "eplanmcp-translation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        List<PlannedChange> changes;
        List<string> kept;
        try
        {
            TranslationTable current = TranslationWrite.Export(writer, Path.Combine(folder, "current.txt"), table.ReferenceLanguage);
            Languages(table, current);
            (changes, kept) = table.Classify(current);
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }
        catch (Exception ex)
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "The translation database could not be read, so the write cannot be planned; nothing was changed.", ex));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        var details = new Dictionary<string, string>
        {
            ["keywords"] = table.Rows.Count.ToString(CultureInfo.InvariantCulture),
            ["referenceLanguage"] = table.ReferenceLanguage,
            ["languages"] = string.Join(", ", table.Columns.Where(column => column != table.ReferenceLanguage)),
            ["kept"] = kept.Count.ToString(CultureInfo.InvariantCulture),
        };
        if (kept.Count > 0)
        {
            // Named in full, because the engineer approving the write cannot see them anywhere else.
            details["keptTranslations"] = string.Join("\n", kept);
        }

        return new WritePlan
        {
            Fingerprint = Fingerprint(table),
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Translations, Database = database.Database, Path = database.Path },
            Changes = changes,
            Details = details,
        };
    }

    /// <summary>
    /// An import fills languages the database already has; it adds no column. Saying which languages it has is more
    /// use than the file-shaped error the remapping would raise.
    /// </summary>
    private static void Languages(TranslationTable table, TranslationTable current)
    {
        string[] unknown = [.. table.Columns.Where(column => !current.Columns.Contains(column, StringComparer.Ordinal))];
        if (unknown.Length > 0)
        {
            throw WriteArguments.NotStarted(WriteArguments.Invalid(
            [
                "The translation database has no language " + string.Join(", ", unknown)
                + "; it has " + string.Join(", ", current.Columns.Where(column => column != "COMMENT"))
                + ". Ask the engineer to add the language in EPLAN's translation settings first.",
            ]));
        }
    }

    /// <summary>
    /// The requested table itself, so a changed request makes the plan stale even on the same database. Reordering the
    /// keywords is not a change, because the import's result does not depend on their order.
    /// </summary>
    internal static string Fingerprint(TranslationTable table) => Operations.Fingerprint.Of(
        [string.Join("\t", table.Columns), .. table.Rows.Select(row => string.Join("\t", row))]);

    internal static (TranslationTable Table, ConfiguredDatabase Database) Check(TranslationKeywordSetArgs args, ITranslationDatabaseWriter writer)
        => (TranslationTable.FromKeywords(args.Keywords, args.ReferenceLanguage), TranslationWrite.Database(writer));
}
