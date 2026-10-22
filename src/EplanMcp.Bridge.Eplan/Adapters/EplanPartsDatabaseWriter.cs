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
/// </summary>
internal sealed class EplanPartsDatabaseWriter : IPartsDatabaseWriter
{
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
