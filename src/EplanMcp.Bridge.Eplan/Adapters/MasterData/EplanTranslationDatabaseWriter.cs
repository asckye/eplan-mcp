using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Configured file database only; quiet import with one reference language, without a transaction (A47).</summary>
internal sealed class EplanTranslationDatabaseWriter : ITranslationDatabaseWriter
{
    public ConfiguredDatabase Configured()
    {
        MainThread.Assert();
        string database = new EplanMasterDataBackup().TranslationDatabase();
        string path = PathMap.SubstitutePath(database);
        return new ConfiguredDatabase { Database = database, Path = path.Contains("$(") ? string.Empty : path };
    }

    public bool Export(string file) => new EplanMasterDataExporter().ExportTranslations(file, MasterDataFormat.Tab);

    public bool Import(string file, string referenceLanguage)
    {
        MainThread.Assert();
        using var translate = new Translate();
        using (new QuietModeStep(QuietModes.ShowNoDialogs))
        {
#pragma warning disable RS0030 // Approved and backed up by the Host (ADR-0019); A47 requires one reference language.
            return translate.ImportDatabaseTexts(file, "XTrLanguageDbXml2TabConverterImpl", referenceLanguage);
#pragma warning restore RS0030
        }
    }
}
