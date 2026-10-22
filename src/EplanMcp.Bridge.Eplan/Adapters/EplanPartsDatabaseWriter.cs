using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The configured parts database for the master-data writes (ADR-0019), read the way spike S24 did (api-notes A27):
/// <c>OpenDatabase()</c> opens the database the settings name, <c>Parts</c> returns every part, each variant on its own
/// (464 for the sample database in 0.07 s), and <c>Close()</c> always follows. Nothing selects, creates or upgrades a
/// database or sets a setting; BannedSymbols.txt keeps it so. Main thread, inside the dispatcher's LockingStep.
/// <para>
/// The only place that imports into it (BannedSymbols.txt), as S24 r13 did (api-notes A30):
/// <c>ImportParts(file, "XPamImportXml", mode, false, identifier, "")</c> outside any transaction (in one it leaves an
/// undo log open and is not rolled back, S24 r11), in QuietModeStep(ShowNoDialogs), since it goes through the user
/// interface's import action, which in r11 tried to open a system error dialog. The empty field mapping scheme is "the
/// one the user interface used last" (EPLAN XML docs); S24 verified only files eplan_part_export wrote, and only
/// AppendNewRecords with PartNumber.
/// </para>
/// </summary>
internal sealed class EplanPartsDatabaseWriter : IPartsDatabaseWriter
{
    /// <summary>The converter of parts management XML (S24 r11).</summary>
    private const string Converter = "XPamImportXml";

    public ConfiguredDatabase Configured()
    {
        MainThread.Assert();
        string database = MDPartsManagement.SelectedPartsDatabaseAsString ?? string.Empty;
        string path = string.Empty;
        if (database.Length > 0 && !MasterDatabases.IsServer(database))
        {
            try
            {
                // A variable EPLAN cannot resolve comes back unchanged (as $(MD_MACROS) in EplanMacroReader).
                string expanded = PathMap.SubstitutePath(database);
                path = expanded.Contains("$(") || !Path.IsPathRooted(expanded) ? string.Empty : expanded;
            }
            catch (BaseException)
            {
                // The path only names the database in the approval and keys its suspension; the setting stands in.
            }
        }

        return new ConfiguredDatabase { Database = database, Path = path };
    }

    public PartsDatabaseState Read()
    {
        MainThread.Assert();
        return Opened(database =>
        {
            MDPart[] parts = database.Parts;
            return new PartsDatabaseState { PartCount = parts.Length, PartNumbers = new HashSet<string>(parts.Select(part => part.PartNr ?? string.Empty), StringComparer.Ordinal) };
        });
    }

    public void Import(string file, string mode, string matchBy)
    {
        MainThread.Assert();
        MDPartsDatabase.ImportMode importMode = mode switch
        {
            "update_existing" => MDPartsDatabase.ImportMode.UpdateExistingRecords,
            "update_and_append" => MDPartsDatabase.ImportMode.UpdateAndAppend,
            "replace_and_append" => MDPartsDatabase.ImportMode.ReplaceAndAppend,
            _ => MDPartsDatabase.ImportMode.AppendNewRecords,
        };
        MDPartsDatabase.ImportIdentifier identifier = matchBy switch
        {
            "unique_id" => MDPartsDatabase.ImportIdentifier.UniqueId,
            "unique_id_or_part_number" => MDPartsDatabase.ImportIdentifier.UniqueIdOptPartNumber,
            _ => MDPartsDatabase.ImportIdentifier.PartNumber,
        };

        // Not wrapped in Opened: a failed import must reach the operation as it is, which reports OUTCOME_UNKNOWN.
        using MDPartsDatabase database = new MDPartsManagement().OpenDatabase() ?? throw new InvalidOperationException("EPLAN opened no parts database.");
        try
        {
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
#pragma warning disable RS0030 // eplan_part_import's change, approved and backed up first (ADR-0019); never in a transaction (S24 r11).
                database.ImportParts(file, Converter, importMode, false, identifier, string.Empty);
#pragma warning restore RS0030
            }
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>Runs <paramref name="read"/> on the opened configured database and closes it; EPLAN's failures are EPLAN_OPERATION_FAILED.</summary>
    private static T Opened<T>(Func<MDPartsDatabase, T> read)
    {
        try
        {
            using MDPartsDatabase database = new MDPartsManagement().OpenDatabase() ?? throw new InvalidOperationException("EPLAN opened no parts database.");
            try
            {
                return read(database);
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
                "EPLAN could not open or read its parts database, so nothing was changed. metadata.eplanMessage has EPLAN's message; ask the engineer to check the configured database in EPLAN's parts management.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}
