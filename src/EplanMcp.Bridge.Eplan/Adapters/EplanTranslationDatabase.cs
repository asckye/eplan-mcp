using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads the configured translation database for translation.keyword.search. <c>Open(bReadOnly: true)</c> opens the
/// database EPLAN's settings name — it takes a flag, not a path, so no path ever reaches it — <c>FindAllKeywords</c>
/// returns the matching keywords with their languages, and <c>Close()</c> always follows. Nothing else is called: no
/// database is created, selected or opened exclusively, and the setting that names it is never written
/// (<c>Translate.TranslateDatabase</c> is only read, as the master-data exporter reads it; api-notes A27).
/// Main thread, inside the dispatcher's LockingStep.
/// <para>
/// Left to the Large tests (docs/tools/eplan_translation_keyword.md): which language <c>FindAllKeywords</c> searches
/// when the caller omits one, how a keyword without a translation in a language reads back, and how long a database
/// with tens of thousands of keywords takes.
/// </para>
/// </summary>
internal sealed class EplanTranslationDatabase : ITranslationDatabase
{
    /// <summary>What a translation database on SQL Server is called; its connection string may hold a login (SEC-12).</summary>
    private const string SqlServer = "SQL Server";

    public TranslationDatabaseContent Read(string query)
    {
        MainThread.Assert();
        string configured = Configured();
        if (configured == SqlServer)
        {
            throw new BridgeErrorException(
                ErrorReason.CapabilityMissing,
                "EPLAN is configured with a translation database on SQL Server, which this tool does not read. Configure a file database in EPLAN's translation settings, or use eplan_translation_export.");
        }

        try
        {
            using var database = new MDTranslationDatabase();
            if (!database.Open(bReadOnly: true))
            {
                throw new InvalidOperationException("EPLAN did not open its translation database.");
            }

            try
            {
                return new TranslationDatabaseContent
                {
                    Database = configured,
                    Languages = [.. Codes(database.Languages)],
                    Keywords = [.. Keywords(database, query)],
                };
            }
            finally
            {
                database.Close();
            }
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or InvalidOperationException or IOException
            && new EplanExceptionMapper().Map(ex) is null)
        {
            WriteExceptionDiagnostics.Report(ex);
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not open or read its translation database. metadata.eplanMessage has EPLAN's message; ask the engineer to check the translation database in EPLAN's settings.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }

    /// <summary>The database EPLAN's settings name, path variables unexpanded, as the master-data exporter reads it.</summary>
    private static string Configured()
    {
        using var translate = new Translate();
        return translate.ActiveTranslateDatabase == Translate.DatabaseType.SQL ? SqlServer : translate.TranslateDatabase ?? string.Empty;
    }

    private static IEnumerable<TranslationKeywordItem> Keywords(MDTranslationDatabase database, string query)
    {
        // The search language is the database's own; the keyword's own language list then says what it holds.
        var texts = new List<MultiLangString>();
        var comments = new List<string>();
        int found = database.FindAllKeywords(query, ISOCode.Language.L___, ref texts, ref comments);
        _ = comments;
        if (found <= 0 || texts is null)
        {
            yield break;
        }

        foreach (MultiLangString text in texts)
        {
            using (text)
            {
                var item = new TranslationKeywordItem { Source = text.GetAsString() ?? string.Empty };
                var languages = new LanguageList();
                _ = text.GetLanguageList(ref languages);
                foreach (string code in Codes(languages))
                {
                    using var iso = new ISOCode(code);
                    if (text.GetString(iso.GetNumber()) is { Length: > 0 } translated)
                    {
                        item.Translations[code] = translated;
                    }
                }

                yield return item;
            }
        }
    }

    /// <summary>The language codes of a list, as EPLAN writes them (<c>zh_CN</c>); an empty code is skipped.</summary>
    private static IEnumerable<string> Codes(LanguageList languages)
    {
        if (languages is null)
        {
            yield break;
        }

        using var iso = new ISOCode();
        for (int index = 0; ; index++)
        {
            ISOCode.Language language;
            try
            {
                // A named parameterized property, which C# reaches through the accessor (CS1546), as the generated
                // api_try dispatch does for the same shape.
                language = languages.get_Language(index);
            }
            catch (Exception ex) when (ex is BaseException or ArgumentException or IndexOutOfRangeException)
            {
                yield break;
            }

            if (iso.GetString(language) is not { Length: > 0 } code)
            {
                yield break;
            }

            yield return code;
        }
    }
}
