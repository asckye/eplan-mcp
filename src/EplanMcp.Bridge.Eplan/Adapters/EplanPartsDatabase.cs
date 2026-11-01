using Eplan.EplApi.Base;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads the configured parts database for part.search the way spike S24 did (api-notes A27): <c>OpenDatabase()</c>
/// opens the database the settings name, <c>Parts</c> returns every part, and <c>Close()</c> always follows. Nothing
/// else is called: no database is created, upgraded (<c>UpdateScheme</c>) or selected, and no setting is set
/// (<c>PartsService.PartsDatabase</c> changes the global user setting, EPLAN XML docs); BannedSymbols.txt keeps it so.
/// S24 saw only the lock file change. Main thread, inside the dispatcher's LockingStep.
/// <para>
/// Left to the Large tests (docs/tools/eplan_part_search.md, section 5): how long <c>Parts</c> and the descriptions
/// take in a database of tens of thousands of parts (then <c>GetParts</c> with a property list preloads only these
/// fields, as the XML recommends), and a database on SQL Server, which S24 did not try.
/// </para>
/// </summary>
internal sealed class EplanPartsDatabase : IPartsDatabase
{
    public PartsDatabaseContent Read(string? language, IReadOnlyList<string>? buildNumbers = null)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        string selected = MDPartsManagement.SelectedPartsDatabaseAsString ?? string.Empty;
        try
        {
            using MDPartsDatabase database = new MDPartsManagement().OpenDatabase() ?? throw new InvalidOperationException("EPLAN opened no parts database.");
            try
            {
                return new PartsDatabaseContent { Database = selected, Parts = [.. database.Parts.Where(p => buildNumbers is null || buildNumbers.Contains(p.PartNr, StringComparer.Ordinal)).Select(part => { PartSearchItem item = Read(part, textLanguage); if (buildNumbers is not null) { item.BuildValues = BuildValues(part); } return item; })] };
            }
            finally
            {
                database.Close();
            }
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or InvalidOperationException or IOException
            && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not open or read its parts database. metadata.eplanMessage has EPLAN's message; ask the engineer to check the configured database in EPLAN's parts management.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }

    /// <summary>
    /// The fields S24 read. The single-language ones are read with ToString as S24 did; the description, which is
    /// multi-language, as project text is (<see cref="EplanText"/>), never by parsing the raw string.
    /// </summary>
    private static PartSearchItem Read(MDPart part, TextLanguage language)
    {
        (string? description, string? descriptionLanguage) = EplanText.ReadWithLanguage(part.Properties.ARTICLE_DESCR1, language);
        return new PartSearchItem
        {
            PartNumber = part.PartNr ?? string.Empty,
            Variant = string.IsNullOrEmpty(part.Variant) ? null : part.Variant,
            Manufacturer = Plain(part.Properties.ARTICLE_MANUFACTURER),
            TypeNumber = Plain(part.Properties.ARTICLE_TYPENR),
            OrderNumber = Plain(part.Properties.ARTICLE_ORDERNR),
            ProductGroup = part.ProductGroup.ToString(),
            Description = description,
            DescriptionLanguage = descriptionLanguage,
            HasMacro = Plain(part.Properties.ARTICLE_MACRO) is not null,
        };
    }

    private static string? Plain(MDPropertyValue value) => value.IsEmpty ? null : value.ToString() is { Length: > 0 } text ? text : null;

    // S24 exported schema (A30); typed numeric conversions follow the installed MDPropertyValue XML documentation.
    private static Dictionary<string, string> BuildValues(MDPart part)
    {
        var p = part.Properties;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["manufacturer"] = Plain(p.ARTICLE_MANUFACTURER) ?? "",
            ["type_number"] = Plain(p.ARTICLE_TYPENR) ?? "",
            ["order_number"] = Plain(p.ARTICLE_ORDERNR) ?? "",
            ["supplier"] = Plain(p.ARTICLE_SUPPLIER) ?? "",
            ["product_group"] = p.ARTICLE_PRODUCTGROUP.IsEmpty ? "" : p.ARTICLE_PRODUCTGROUP.ToInt().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["product_subgroup"] = p.ARTICLE_PRODUCTSUBGROUP.IsEmpty ? "" : p.ARTICLE_PRODUCTSUBGROUP.ToInt().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["width"] = p.ARTICLE_WIDTH.IsEmpty ? "" : p.ARTICLE_WIDTH.ToDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["height"] = p.ARTICLE_HEIGHT.IsEmpty ? "" : p.ARTICLE_HEIGHT.ToDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["depth"] = p.ARTICLE_DEPTH.IsEmpty ? "" : p.ARTICLE_DEPTH.ToDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["technical_data"] = Plain(p.ARTICLE_CHARACTERISTICS) ?? "",
            ["macro_file"] = Plain(p.ARTICLE_MACRO) ?? "",
            ["picture_file"] = Plain(p.ARTICLE_PICTUREFILE) ?? "",
            ["part_type"] = p.ARTICLE_PARTTYPE.IsEmpty ? "1" : p.ARTICLE_PARTTYPE.ToInt().ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (!p.ARTICLE_DESCR1.IsEmpty)
        {
            foreach (string code in TextLanguages.Stored(p.ARTICLE_DESCR1.ToString()))
            { values["description." + code] = EplanText.ReadWithLanguage(p.ARTICLE_DESCR1, EplanText.Language(code)).Text ?? ""; }
        }
        MDFunctionTemplatePosition[] templates = part.FunctionTemplatePositions;
        values["function_category"] = templates.Length == 0 ? "" : templates.Length > 1 ? "multiple" : ((int)templates[0].FunctionDefinitionCategory).ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Take the template's property list once (api-notes: repeated Properties per wrapper is a known crash pattern).
        var template = templates.Length == 1 ? templates[0].Properties : null;
        values["connection_points"] = template is null ? "" : string.Join("|", Enumerable.Range(1, 1000).Select(i => Plain(template.get_FUNCTEMPLATE_CONNPT_DESIGNATION(i)) ?? "")).TrimEnd('|');
        return values;
    }
}
