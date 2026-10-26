using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters.Generated;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The master-data backup of ADR-0019 the way spike S24 made it in the dispatcher (api-notes A30): EPLAN's
/// <c>backup</c> action through <c>ActionManager</c>, each parameter added on its own, never a command line, so
/// paths with any characters pass unchanged (S24: the Remote Client garbled Chinese paths, in-process calls did not).
/// <c>SOURCEPATH</c> and <c>FILENAME</c> are split from the configured database, path variables left for EPLAN to
/// expand. This is the only place that runs an EPLAN action (BannedSymbols.txt). The backup changes no database file
/// (S24: SHA-256 of both databases unchanged). QuietModeStep(ShowNoDialogs) is added so that no dialog can stop the
/// main thread; S24 ran the action without it, so the Large tests confirm it still makes the archive.
/// Main thread, inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanMasterDataBackup : IMasterDataBackup
{
    public string PartsDatabase()
    {
        MainThread.Assert();
        return MDPartsManagement.SelectedPartsDatabaseAsString ?? string.Empty;
    }

    public void BackupParts(string database, string folder, string archiveName)
        => Backup("ARTICLES", database, folder, archiveName);

    public string TranslationDatabase()
    {
        MainThread.Assert();
        using var translate = new Translate();
        return translate.ActiveTranslateDatabase == Translate.DatabaseType.SQL
            ? throw MasterDatabases.Denied("translation")
            : translate.TranslateDatabase ?? string.Empty;
    }

    public void BackupTranslations(string database, string folder, string archiveName)
        => Backup("LANGUAGES", database, folder, archiveName);

    private static void Backup(string masterDataType, string database, string folder, string archiveName)
    {
        MainThread.Assert();
        // The parameters as the generated class has them from the action catalog (catalog/wrappers.yaml), in the same
        // order as before; TYPE and the action's name are written here, so the coverage scan sees A:backup/MASTERDATA.
        var parameters = new BackupMasterDataParameters
        {
            MDTYPE = masterDataType,
            SOURCEPATH = Path.GetDirectoryName(database) ?? string.Empty,
            FILENAME = Path.GetFileName(database),
            DESTINATIONPATH = folder,
            ARCHIVENAME = archiveName,
        };
        using var context = new ActionCallingContext();
        context.AddParameter("TYPE", BackupMasterDataParameters.Type);
        parameters.AddTo(context);
        bool done;
        using (new QuietModeStep(QuietModes.ShowNoDialogs))
        {
#pragma warning disable RS0030 // The backup of ADR-0019 and of eplan_part_export's backup format; it writes only into the Host's new folder.
            var action = new ActionManager().FindAction(BackupMasterDataParameters.Action) ?? throw new InvalidOperationException("EPLAN has no 'backup' action.");
#pragma warning restore RS0030
            done = action.Execute(context);
        }

        if (!done)
        {
            throw new InvalidOperationException("EPLAN's backup action returned false; EPLAN's system messages may say why.");
        }
    }
}
