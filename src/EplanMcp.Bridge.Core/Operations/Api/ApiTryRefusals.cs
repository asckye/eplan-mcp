using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A77: crash evidence is checked before resolving any target or invoking a native member.</summary>
public static class ApiTryRefusals
{
    public static void Check(string group, bool setter)
    {
        string? method = ApiTryCatalog.MethodAccess(group);
        if (ApiTryCatalog.Refused(group, method ?? (setter ? "write" : "read")) is not null
            || method is not null && ApiTryCatalog.Refused(group, "call") is not null)
        {
            throw new BridgeErrorException(ErrorReason.CapabilityMissing, ApiTryCatalog.RefusalMessage)
            { Error = { Execution = ExecutionState.NotStarted } };
        }
    }
}
