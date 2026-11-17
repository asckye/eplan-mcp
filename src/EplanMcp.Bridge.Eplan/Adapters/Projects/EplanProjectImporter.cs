using System.Security;
using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Imports an EPJ (PXF) file as a new project the way spike S22 did in the dispatcher (api-notes A29):
/// <c>new Import().PXFProject(file, target)</c>, the target being the new project's path without extension. The overload
/// with a third argument may synchronise parts with the system parts database (EPLAN XML docs), so it is not used. Main
/// thread; there is no transaction: the operation deletes what a failed import made.
/// </summary>
internal sealed class EplanProjectImporter : IProjectImporter
{
    public void ImportPxf(string filePath, string target)
    {
        MainThread.Assert();
        try
        {
            using var import = new Import();
            import.PXFProject(filePath, target);
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or ApplicationException or NotSupportedException or SecurityException or IOException
            && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not import the EPJ file as a project. metadata.eplanMessage has EPLAN's message; the file may be damaged, or from a newer EPLAN.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}
