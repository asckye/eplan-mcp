using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>EPLAN's "reassign all" for cables through <c>CableService.DoReassignWires</c> (api-notes A108): a connection gets a core template of the same potential type.</summary>
internal sealed class EplanCableReassigner : ICableReassigner
{
    public void Reassign(string projectPath, IReadOnlyList<string>? cables, bool overwrite)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var service = new CableService();
        try
        {
            using var finder = new DMObjectsFinder(project);
            using var filter = new FunctionsFilter { FunctionCategory = FunctionCategory.Cable };
            Cable[] scope = [.. EplanNativePreconditions.Valid(finder.GetFunctions(filter))
                .Where(f => f.IsMainFunction && (cables is null || cables.Contains(f.Name ?? string.Empty, StringComparer.Ordinal)))
                .OfType<Cable>()];
            if (cables is null)
            { service.DoReassignWires(project, overwrite, null); }
            else
            {
                foreach (Cable cable in scope)
                { service.DoReassignWires(cable, overwrite, null); }
            }
            foreach (Cable cable in scope)
            { DropEchoedDesignations(cable); }
        }
        catch (Exception ex) when (ex is BaseException or ApplicationException or ArgumentException)
        {
            WriteExceptionDiagnostics.Report(ex);
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not assign the cores, so nothing was committed: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// A connection designation (31011) equal to the core's colour or number (31004) that the part just gave it is
    /// shown twice at the definition point, one label above the other (redraw of 2026-11-22: every cable page had a
    /// second row "1 2 3 GNYE"). The core's own value stays; only the echo is cleared. The definition point's
    /// designation cannot be set (S063113), so it is cleared on the connection.
    /// </summary>
    private static void DropEchoedDesignations(Cable cable)
    {
        foreach (Connection connection in cable.CableConnections)
        {
            // One property list per wrapper (api-notes: repeated Properties per wrapper is a known crash pattern).
            ConnectionPropertyList properties = connection.Properties;
            string? designation = Text(properties, 31011), core = Text(properties, 31004);
            if (designation is not null && string.Equals(designation, core, StringComparison.Ordinal))
            { properties[(Properties.Connection)31011] = string.Empty; }
        }
    }

    private static string? Text(ConnectionPropertyList properties, int number)
    {
        using AnyPropertyId id = number;
        using PropertyValue value = properties[id];
        return value.IsEmpty ? null : value.ToString();
    }
}
