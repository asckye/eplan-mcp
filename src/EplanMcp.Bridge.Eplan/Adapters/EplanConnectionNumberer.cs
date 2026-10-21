using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.HEServices.Exceptions;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Numbers connections the way spike S21 did (api-notes A24): <c>ConnectionService.DesignateConnections</c> on the whole
/// project, the overload S21 used, neither marking the new wire numbers as manual nor generating connections first; the
/// schemes from the project setting S21 found. Main thread, inside the dispatcher's LockingStep; <see cref="Designate"/>
/// inside the write's TransactionScope, whose SafetyPoint S21 saw roll the numbering back.
/// </summary>
internal sealed class EplanConnectionNumberer : IConnectionNumberer
{
    private const string SchemeNode = "WireNumberingLog.Schema";

    public IReadOnlyList<string> Schemes(string projectPath) => EplanSchemes.OfProject(SchemeNode, projectPath);

    /// <summary>
    /// Every connection from <c>GetConnections(null)</c>, as S21 compared them. The wire number is a language-independent
    /// value, read as the connection reader reads one (S6); a 3D connection has properties but no page (S6).
    /// </summary>
    public IReadOnlyList<DesignatedConnection> ReadConnections(string projectPath)
    {
        MainThread.Assert();
        TextLanguage language = EplanText.Language(null);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return [.. finder.GetConnections(null).Select(connection => new DesignatedConnection
        {
            Id = connection.ToStringIdentifier(),
            Designation = EplanText.Read(connection.Properties.CONNECTION_DESIGNATION, language),
            Page = EplanConnectionReader.PageOf(connection)?.Name,
        })];
    }

    /// <summary>
    /// <paramref name="overwrite"/> as <c>DesignateOverwrition</c> (tool spec 5; S21 used <c>All</c>, the others are not
    /// verified). EPLAN's InvalidScheme names the schemes it has; other EPLAN failures go back as EPLAN_OPERATION_FAILED
    /// with EPLAN's message.
    /// </summary>
    public void Designate(string projectPath, string scheme, string overwrite)
    {
        MainThread.Assert();
        ConnectionService.DesignateOverwrition mode = overwrite switch
        {
            "none" => ConnectionService.DesignateOverwrition.None,
            "all" => ConnectionService.DesignateOverwrition.All,
            _ => ConnectionService.DesignateOverwrition.ExceptManuals,
        };
        using var service = new ConnectionService();
        try
        {
            service.DesignateConnections(EplanProjects.ByPath(projectPath), scheme, mode, false, false);
        }
        catch (InvalidScheme ex)
        {
            throw SchemeChoice.Refused(scheme, ex.Message, "wire", ex);
        }
        catch (Exception ex) when (ex is BaseException or ApplicationException or ArgumentException && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not number the connections, so nothing was numbered. metadata.eplanMessage has EPLAN's message.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}
