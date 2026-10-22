using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>S34 r3 (A39): borrowed StorableObjects are never disposed; each Properties list is acquired once (A36).</summary>
internal sealed class EplanCableGenerator : ICableGenerator
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Schemes(string projectPath) => new Dictionary<string, IReadOnlyList<string>>
    {
        ["creationScheme"] = SchemeChoice.Offered(EplanSchemes.OfProject("CableLog.CableCreateScheme", projectPath)),
        ["numberingScheme"] = SchemeChoice.Offered(EplanSchemes.OfProject("CableLog.CableNumberingScheme", projectPath)),
        ["selectionScheme"] = SchemeChoice.Offered(EplanSchemes.OfProject("CableLog.CableAutoSelectionScheme", projectPath)),
    };

    public CableGenerationSnapshot Read(string projectPath)
    {
        MainThread.Assert();
        try
        {
            Project project = EplanProjects.ByPath(projectPath);
            using var finder = new DMObjectsFinder(project);
            using var filter = new FunctionsFilter { FunctionCategory = FunctionCategory.Cable };
            Dictionary<string, string> connections = finder.GetConnections(null).ToDictionary(c => c.ToStringIdentifier(), ConnectionState, StringComparer.Ordinal);
            var cables = new List<CableGenerationCable>();
            foreach (Function function in finder.GetFunctions(filter))
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                var cable = (Cable)function;
                using FunctionPropertyList properties = function.Properties;
                using PropertyValue automatic = properties.FUNC_CABLEAUTOCREATED;
                string business = Hash(function.VisibleName, function.IsMainFunction, function.Page?.Name,
                    Raw(properties.FUNC_CABLETYPE), Raw(properties.FUNC_CABLEWIRECOUNT), Raw(properties.FUNC_CABLEWIRECROSSSECTION), Raw(properties.FUNC_CABLELENGTH_FULL),
                    function.ArticleReferences.Select(r => r.PartNr).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                    cable.CableConnections.Select(c => Hash(c.ToStringIdentifier(), connections.TryGetValue(c.ToStringIdentifier(), out string state) ? state : null)).OrderBy(s => s, StringComparer.Ordinal).ToArray());
                cables.Add(new CableGenerationCable(function.ToStringIdentifier(), function.Name, automatic.IsEmpty ? null : automatic.ToBool(), business));
            }

            string[] objects = [.. finder.GetPages(null).Select(p => Hash(p.ToStringIdentifier(), p.Name, p.AllPlacements.Select(o => o.ToStringIdentifier()).OrderBy(s => s, StringComparer.Ordinal).ToArray()))];
            return new CableGenerationSnapshot(cables, connections.Count, Hash(objects.OrderBy(s => s, StringComparer.Ordinal).ToArray(), connections.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()));
        }
        catch (Exception ex) when (ex is not BridgeErrorException && new EplanExceptionMapper().Map(ex) is null)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Required cable generation readback failed. Ask the engineer to check the project and schemes before trying again.", ex);
        }
    }

    public void Generate(string projectPath, string creationScheme, string numberingScheme, string selectionScheme)
    {
        MainThread.Assert();
        try
        {
            using var context = new ActionCallingContext();
            context.AddParameter("TYPE", "CABLES");
            context.AddParameter("PROJECTNAME", EplanProjects.ByPath(projectPath).ProjectLinkFilePath);
            context.AddParameter("CREATIONSCHEME", creationScheme);
            context.AddParameter("NUMBERINGSCHEME", numberingScheme);
            context.AddParameter("AUTOSELECTSCHEME", selectionScheme);
            context.AddParameter("KEEPOLDNAMES", "1");
            context.AddParameter("ONLYAUTOCABLES", "1");
            context.AddParameter("STARTVALUE", "1");
            context.AddParameter("STEPVALUE", "1");
#pragma warning disable RS0030 // S34 generate/CABLES only, with fixed safety parameters, after backup and inside TransactionScope (tool spec 5).
            var action = new ActionManager().FindAction("generate") ?? throw new InvalidOperationException("EPLAN has no 'generate' action.");
#pragma warning restore RS0030
            if (!action.Execute(context))
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused automatic cable generation. Ask the engineer to check the schemes and EPLAN messages.");
            }
        }
        catch (Exception ex) when (ex is not BridgeErrorException && new EplanExceptionMapper().Map(ex) is null)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not generate automatic cables. Ask the engineer to check the schemes and EPLAN messages.", ex);
        }
    }

    /// <summary>A Connection3D throws NotSupportedException on StartPin and EndPin and has no page (S6), so only its properties count.</summary>
    private static string ConnectionState(Connection connection)
    {
        using ConnectionPropertyList properties = connection.Properties;
        bool is3D = connection is Connection3D;
        return Hash(is3D ? null : connection.Page?.Name, is3D ? null : End(connection.StartPin), is3D ? null : End(connection.EndPin),
            Raw(properties.CONNECTION_DESIGNATION), Raw(properties.CONNECTION_WIRECROSSSECTION), Raw(properties.CONNECTION_WIRENUMBER));
    }

    private static string? End(Pin? pin) => pin is null ? null : Hash(pin.ParentFunction?.ToStringIdentifier(), pin.ParentFunction?.Name, pin.Name);

    private static string? Raw(PropertyValue value)
    {
        using (value)
        {
            return value.IsEmpty ? null : value.ToString();
        }
    }

    private static string Hash(params object?[] values) => Fingerprint.Of([BridgeJson.FromObject(values).ToString(Formatting.None)]);
}
