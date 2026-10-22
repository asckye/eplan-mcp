using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>S34's project list actions and counts (A39). Main thread; Import inside the write's TransactionScope.</summary>
internal sealed class EplanListImporter : IListImporter
{
    public ListImportState Read(string projectPath)
    {
        MainThread.Assert();
        try
        {
            Project project = EplanProjects.ByPath(projectPath);
            using var finder = new DMObjectsFinder(project);
            // A39: these StorableObjects belong to EPLAN. Dispose only the finder and the context, never its results.
            var entries = finder.GetAll<DeviceListEntry>(true).Cast<DeviceListEntry>().ToArray();
            Article[] articles = project.Articles;
            ArticleReference[] references = finder.GetArticleReferences(null);
            IEnumerable<string> lines = finder.GetPages(null).Select(p => Line("page", p.ToStringIdentifier(), p.Name))
                .Concat(finder.GetFunctions(null).Select(f => Line("function", f.ToStringIdentifier(), f.Name)))
                .Concat(entries.Select(e => Line("entry", e.ObjectIdentifier, e.PartNumber)))
                .Concat(articles.Select(a => Line("part", a.ToStringIdentifier(), a.PartNr, a.VariantNr)))
                .Concat(references.Select(r => Line("reference", r.ToStringIdentifier(), r.PartNr, r.VariantNr, r.Count, r.ParentObject?.ToStringIdentifier())));
            return new ListImportState(new ListImportCounts
            {
                DeviceListEntries = entries.Length,
                ProjectParts = articles.Length,
                ArticleReferences = references.Length,
            }, Fingerprint.Of(lines));
        }
        catch (Exception ex) when (ex is not BridgeErrorException && new EplanExceptionMapper().Map(ex) is null)
        {
            throw Failed(ex);
        }
    }

    public bool Import(string projectPath, string kind, string filePath, string converter)
    {
        MainThread.Assert();
        if ((kind, converter) is not (("devices", "XDLXmlExporter") or ("devices", "XDLCsvImporterExporter") or ("parts", "XPalXmlExporter")))
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument, "Use devices XML/CSV or parts XML for list import.");
        }

        try
        {
            using var context = new ActionCallingContext();
            context.AddParameter("TYPE", "IMPORT");
            context.AddParameter("PROJECTNAME", projectPath);
            context.AddParameter("IMPORTFILE", filePath);
            context.AddParameter("FORMAT", converter);
#pragma warning disable RS0030 // S34, A39: fixed project list imports only; no system parts database import.
            var action = kind == "devices" ? new ActionManager().FindAction("devicelist") : new ActionManager().FindAction("partslist");
#pragma warning restore RS0030
            return action is not null && action.Execute(context);
        }
        catch (Exception ex) when (ex is not BridgeErrorException && new EplanExceptionMapper().Map(ex) is null)
        {
            throw Failed(ex);
        }
    }

    private static string Line(params object?[] fields) => JsonConvert.SerializeObject(fields);

    private static BridgeErrorException Failed(Exception cause) => new(ErrorReason.EplanOperationFailed,
        "EPLAN could not import or read back the project list. Check the file and this call's EPLAN messages; keep the project backup.", cause);
}
