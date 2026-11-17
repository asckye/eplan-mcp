using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Read-only predicates before any transaction or trial invocation; official XML and live reports: A64.</summary>
internal static class EplanNativePreconditions
{
    internal static bool HasPage(StorableObject obj) => obj.IsValid && obj is Placement placement
        && placement.IsPlaced && placement.Page is { IsValid: true };

    internal static void Writable(StorableObject obj)
    {
        if (obj.IsReadOnly || obj.Project is { IsReadOnly: true } || obj is IWriteProtection { WriteProtected: true }
            || obj is Placement { IsPlaced: true } placement && placement.Page is { } page
                && (page.IsReadOnly || page is IWriteProtection { WriteProtected: true }))
        { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.EplanOperationFailed, "The object, owning database, project or page is read-only or write-protected. Choose a writable object before retrying. Nothing changed.")); }
    }

    internal static IEnumerable<T> Valid<T>(IEnumerable<T> objects) where T : StorableObject
    {
        int skipped = 0;
        try
        {
            foreach (T obj in objects)
            {
                if (obj is null || !obj.IsValid)
                { skipped++; continue; }
                yield return obj;
            }
        }
        finally
        {
            if (skipped > 0)
            { OperationContext.Current.Warn(FormattableString.Invariant($"Skipped {skipped} invalid EPLAN objects before reading their properties; enumeration is incomplete.")); }
        }
    }
}
