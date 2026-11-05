using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanPlacementEditor
{
    private static JObject InspectSymbols(string projectPath, string pageName)
    {
        var target = WriteArguments.Writable(new EplanProjectCatalog(), projectPath);
        projectPath = target.Path;
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        Page page = EplanDeviceReader.FindPage(finder, pageName) ?? throw new BridgeErrorException(EplanMcp.Contracts.ErrorReason.PageNotFound, "Page not found.");
        var objects = new JArray();
        foreach (Placement root in EplanNativePreconditions.Valid(page.AllPlacements).Where(p => p is not PropertyPlacement))
        {
            var identity = EplanObjectReader.Describe(root, root.GetType().Name.ToLowerInvariant());
            SymbolVariant? variant = null;
            bool unreadable = false;
            try
            { variant = root is SymbolReference symbol ? symbol.SymbolVariant : null; }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException) { unreadable = true; }
            var row = new JObject { ["locator"] = JObject.FromObject(new EplanMcp.Contracts.DrawingLocator { Project = projectPath, Page = pageName, Type = identity.Type, Oid = identity.Oid, Digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(identity)) }, Bridge.Core.Protocol.BridgeJson.Serializer), ["kind"] = root.GetType().Name, ["location"] = new JArray(root.Location.X, root.Location.Y), ["symbol"] = unreadable ? new JObject() : variant is null ? null : new JObject { ["library"] = variant.SymbolLibraryName, ["name"] = variant.SymbolName, ["variant"] = variant.VariantNr } };
            if (root is SymbolReference reference)
            {
                row["arrangement"] = EplanSymbolArrangement.SelectedName(reference);
                string[]? names = EplanSymbolArrangement.Available(reference);
                row["arrangements"] = names is null ? JValue.CreateNull() : new JArray(names);
                try
                { using UniversalPropertyList properties = EplanObjectReader.Properties(root); row["activePropertySet"] = EplanSymbolArrangement.ReadActive(properties); }
                catch (Exception ex) when (ex is BaseException or InvalidOperationException) { row["activePropertySet"] = null; }
            }
            objects.Add(row);
        }
        return new JObject { ["scene"] = new JObject { ["project"] = new JObject { ["name"] = project.ProjectName, ["path"] = projectPath }, ["page"] = new JObject { ["page"] = pageName } }, ["objects"] = objects };
    }
    private static void EditSymbols(JObject input, JObject before)
    {
        Project project = EplanProjects.ByPath((string)before["scene"]!["project"]!["path"]!);
        using var finder = new DMObjectsFinder(project);
        Page page = EplanDeviceReader.FindPage(finder, (string)input["page"]!)!;
        foreach (JToken locator in input["locators"]!)
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            var root = (SymbolReference)page.AllPlacements.Single(p => p.ObjectIdentifier == (long)locator["oid"]! && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == (int)locator["type"]!);
            EplanNativePreconditions.Writable(root);
            if ((string?)input["operation"] == "set_arrangement")
            { EplanSymbolArrangement.Select(root, (string)input["property_arrangement"]!); continue; }
            SymbolVariant current = root.SymbolVariant;
            if (current.VariantNr == (int)input["symbol_variant"]!)
            { continue; }
            SymbolVariant variant = project.SymbolLibraries.Single(l => l.Name == current.SymbolLibraryName).Symbols.Single(s => s.Name == current.SymbolName).Variants.Single(v => v.VariantNr == (int)input["symbol_variant"]!);
            if (root is Function function)
            { function.SetSymbolVariant(variant, false); }
            else
            { root.SymbolVariant = variant; }
        }
    }
}
