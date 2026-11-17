using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// ADR-0054: parts master data members of <c>eplan_api_try</c>. Reads use any configured database; a write names the
/// configured file database as its master data target, so the Host backs it up first (ADR-0019) and refuses it unless
/// the database is a copy under a scratch root.
/// </summary>
public static class ApiTryMasterData
{
    /// <summary>The message of POLICY_DENIED when no file database is configured; the Host adds the scratch root check.</summary>
    public const string NoFileDatabase =
        "Master data writes through eplan_api_try need EPLAN's parts database to be a file copy under a scratch root (ADR-0054). EPLAN has no file database configured that can be resolved; ask the engineer to configure a scratch copy in EPLAN's parts management. Nothing was changed.";

    /// <summary>True when a member of <paramref name="declaringType"/> changes the parts database.</summary>
    public static bool Changes(string declaringType) => declaringType.StartsWith("Eplan.EplApi.MasterData.", StringComparison.Ordinal);

    /// <summary>The write's target: the configured file database; POLICY_DENIED for SQL Server or when none resolves.</summary>
    public static MasterDataTarget Target(ConfiguredDatabase database)
    {
        if (MasterDatabases.IsServer(database.Database))
        {
            throw MasterDatabases.Denied();
        }

        if (database.Database.Length == 0 || database.Path.Length == 0)
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PolicyDenied, NoFileDatabase));
        }

        return new MasterDataTarget { Kind = MasterDataKinds.Parts, Database = database.Database, Path = database.Path };
    }
}
