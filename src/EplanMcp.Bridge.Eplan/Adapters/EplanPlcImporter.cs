using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>S34's plcservice parameters and complete read-back (A39). Main thread; Import inside TransactionScope.</summary>
internal sealed class EplanPlcImporter : IPlcImporter
{
    public PlcImportState Read(string projectPath)
    {
        MainThread.Assert();
        try
        {
            using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
            Function[] functions = finder.GetFunctions(null);
            var plcs = finder.GetPLCs(null);
            var boxes = new HashSet<long>(plcs.Select(p => p.ObjectIdentifier));
            // A39: never dispose EPLAN-owned StorableObjects; merge the two enumerations by object number, not name.
            var all = functions.Cast<Function>().Concat(plcs).GroupBy(f => f.ObjectIdentifier).Select(g => g.First());
            PlcImportFunction[] rows = [.. all.Select(f => new PlcImportFunction(f.ObjectIdentifier, f.Name ?? throw new InvalidOperationException("A function name could not be read."), f.FunctionCategory.ToString(), boxes.Contains(f.ObjectIdentifier)))];
            return new PlcImportState(rows, new ImportPlcCounts { Functions = functions.Select(f => f.ObjectIdentifier).Distinct().Count(), PlcBoxes = boxes.Count, ArticleReferences = finder.GetArticleReferences(null).Length });
        }
        catch (Exception ex) when (ex is not BridgeErrorException && new EplanExceptionMapper().Map(ex) is null)
        {
            throw Failed(ex);
        }
    }

    public bool Import(string projectPath, string filePath, string converter)
    {
        MainThread.Assert();
        try
        {
            using var context = new ActionCallingContext();
            context.AddParameter("TYPE", "BUSDATAIMPORT");
            context.AddParameter("PROJECTNAME", EplanProjects.ByPath(projectPath).ProjectLinkFilePath);
            context.AddParameter("SOURCEFILE", filePath);
            context.AddParameter("CONVERTERID", converter);
            context.AddParameter("IMPORTMATCH", "0");
            context.AddParameter("LANGUAGE", "de_DE");
#pragma warning disable RS0030 // S34, A39: fixed PLC import only, after backup and inside the write's TransactionScope.
            var action = new ActionManager().FindAction("plcservice") ?? throw new InvalidOperationException("EPLAN has no 'plcservice' action.");
#pragma warning restore RS0030
            return action.Execute(context);
        }
        catch (Exception ex) when (ex is not BridgeErrorException && new EplanExceptionMapper().Map(ex) is null)
        {
            throw Failed(ex);
        }
    }

    private static BridgeErrorException Failed(Exception cause) => new(ErrorReason.EplanOperationFailed,
        "EPLAN could not import or read back the PLC data. Ask the engineer to check the source file and EPLAN's messages; the backup, if made, was retained.", cause);
}
