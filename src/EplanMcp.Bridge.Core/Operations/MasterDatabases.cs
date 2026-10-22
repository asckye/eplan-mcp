using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What the master-data writes know about the configured databases (ADR-0019).</summary>
public static class MasterDatabases
{
    /// <summary>The message of POLICY_DENIED for a database on SQL Server (ADR-0019); it names no server or login.</summary>
    public const string ServerDenied =
        "The parts database EPLAN is configured with is on SQL Server, and this server's safety policy allows writes to file databases only: a SQL Server database cannot be backed up from this computer and may be shared with others. Nothing was changed.";

    /// <summary>
    /// True for a SQL Server connection string (<c>Server|Database|0</c>, or <c>Server|Database|2|User|Password</c>,
    /// EPLAN XML docs of <c>MDPartsManagement.OpenDatabase</c>); a file database never contains the separator.
    /// </summary>
    public static bool IsServer(string database) => database.IndexOf('|') >= 0;

    /// <summary>POLICY_DENIED for a SQL Server database: nothing has run (ADR-0019).</summary>
    public static BridgeErrorException Denied() => WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PolicyDenied, ServerDenied));
}
