using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Independent read-only ArticleReferences evidence on the current LargeWrite page.</summary>
public sealed class TerminalPartReadbackAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeTerminalPartReadback"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }
    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string file = "";
        oActionCallingContext.GetParameter("REQUEST", ref file);
        var result = new JObject { ["success"] = false };
        try
        {
            using var locking = new LockingStep();
            JObject input = JObject.Parse(File.ReadAllText(file));
            Project project = new SelectionSet().GetCurrentProject(false);
            if (!string.Equals(project.ProjectFullName, Path.Combine(DataRoot.Path, "scratch", "writes", "LargeWrite"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(project.ProjectFullName, (string?)input["project"], StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("Only the current scratch LargeWrite may be read."); }
            Page page = project.Pages.Single(p => p.Name == (string?)input["page"]);
            Function function = page.AllPlacements.OfType<Function>().Single(f => f.Name == (string?)input["tag"]
                && f.FunctionCategory.ToString() == ((string?)input["category"] ?? "Terminal")
                && (input["x"] is null || f.Location.X == (double)input["x"]! && f.Location.Y == (double)input["y"]!));
            result["name"] = function.Name;
            result["category"] = function.FunctionCategory.ToString();
            result["main"] = function.IsMainFunction;
            result["visibleName"] = function.VisibleName;
            result["symbol"] = new JObject
            {
                ["library"] = function.SymbolVariant.SymbolLibraryName,
                ["name"] = function.SymbolVariant.SymbolName,
                ["variant"] = function.SymbolVariant.VariantNr
            };
            using (FunctionPropertyList properties = function.Properties)
            {
                using PropertyValue definition = properties[(Properties.Function)20188];
                result["definition"] = definition.ToString();
            }
            result["parts"] = new JArray(function.ArticleReferences.Select(r => new JObject
            { ["part"] = r.PartNr, ["variant"] = r.VariantNr, ["count"] = r.Count }));
            result["success"] = true;
        }
        catch (Exception error) { result["error"] = error.ToString(); }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }
}
