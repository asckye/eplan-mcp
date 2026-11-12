using Eplan.EplApi.Base;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Core.Operations;
namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// ADR-0054: parts master data handles. A database handle comes only from <c>MDPartsManagement.OpenDatabase()</c>,
/// which opens the database EPLAN is configured with. It and every handle read from it stay valid while the database is
/// open and EPLAN is still configured with it; the table closes it once no handle refers to it.
/// </summary>
internal sealed partial class EplanApiTryDispatcher
{
    public ConfiguredDatabase PartsDatabase() => new EplanPartsDatabaseWriter().Configured();

    private static Func<string?> MasterDataOwner(MDPartsDatabase database, Func<string?> owner)
    {
        string configured = MDPartsManagement.SelectedPartsDatabaseAsString ?? string.Empty;
        return () => database.IsOpen && string.Equals(MDPartsManagement.SelectedPartsDatabaseAsString ?? string.Empty, configured, StringComparison.Ordinal) ? owner() : null;
    }

    /// <summary>Main thread: the handle table drops entries only while the dispatcher runs or EPLAN raises its close events.</summary>
    private static void ReleaseNative(object value)
    {
        if (value is not MDPartsDatabase database)
        {
            return;
        }

        try
        {
            if (database.IsOpen)
            {
                database.Close();
            }

            database.Dispose();
        }
        catch (BaseException ex)
        {
            Hosting.StartupFailureLog.Write(ex);
        }
    }
}
