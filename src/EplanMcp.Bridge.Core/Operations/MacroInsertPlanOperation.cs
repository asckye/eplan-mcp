using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.MacroInsertPlan"/>: checks the page, the macro and where its outline goes, and returns one
/// <c>create</c> change with the macro's devices in the details for the dry run (docs/tools/eplan_macro_insert.md,
/// section 5). Reads only: the macro is opened without inserting it, which S10 found leaves the project alone (A14).
/// </summary>
public sealed class MacroInsertPlanOperation(IProjectCatalog catalog, IPageReader pages, IDeviceReader devices, IMacroReader macros, IMacroInserter inserter)
    : Operation<MacroInsertArgs, WritePlan>
{
    public override string Name => OperationNames.MacroInsertPlan;

    protected override WritePlan Execute(MacroInsertArgs args)
    {
        var request = MacroInsertRequest.Check(args, catalog, pages, macros, inserter);
        List<PageDevice> inMacro = request.MacroDevices();
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [request.Change(inMacro.Count)],
            Fingerprint = request.StateFingerprint(devices),
            Details = new Dictionary<string, string>
            {
                ["macro"] = request.Macro,
                ["deviceCount"] = inMacro.Count.ToString(CultureInfo.InvariantCulture),
                ["devices"] = string.Join("\n", inMacro.Take(MacroInsertRequest.MaxDevices).Select(d => d.Tag + "\t" + d.VisibleTag + "\t" + d.Category)),
                ["objectCount"] = request.Content.ObjectCount.ToString(CultureInfo.InvariantCulture),
                ["pageSizeKnown"] = request.Page.Width is null ? "false" : "true",
            },
        };
    }
}
