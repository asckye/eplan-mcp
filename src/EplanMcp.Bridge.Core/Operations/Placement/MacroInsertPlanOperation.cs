using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.MacroInsertPlan"/>: checks the page, the macro and where its outline goes, and returns one
/// <c>create</c> change and requested value-set applications, with the macro's devices in the details for the dry run (docs/tools/eplan_macro_insert.md,
/// section 5). Reads only: the macro is opened without inserting it, which S10 found leaves the project alone (A14).
/// </summary>
public sealed class MacroInsertPlanOperation(IProjectCatalog catalog, IPageReader pages, IDeviceReader devices, IMacroReader macros, IMacroInserter inserter)
    : Operation<MacroInsertArgs, WritePlan>
{
    public override string Name => OperationNames.MacroInsertPlan;

    protected override WritePlan Execute(MacroInsertArgs args)
    {
        List<MacroInsertRequest> requests = MacroInsertRequest.CheckAll(args, catalog, pages, macros, inserter);
        bool batch = args.Items is not null;
        var plan = new WritePlan
        {
            Project = requests[0].Project.Name,
            ProjectPath = requests[0].Project.Path,
            Fingerprint = MacroInsertRequest.FingerprintFor(requests, devices, batch),
        };
        var items = new List<MacroInsertData>();
        foreach (MacroInsertRequest request in requests)
        {
            List<PageDevice> inMacro = request.MacroDevices();
            int objectCount = request.Content.ObjectCount - (request.MacroBox ? 0 : request.Content.MacroBoxCount);
            plan.Changes.Add(request.Change(inMacro.Count));
            plan.Changes.AddRange(request.ValueChanges(includePage: batch));
            if (batch)
            {
                items.Add(new MacroInsertData
                {
                    Page = request.Page.Name,
                    Macro = request.Macro,
                    ValueCount = request.Values?.Count ?? 0,
                    DeviceCount = inMacro.Count,
                    ObjectCount = objectCount,
                });
            }
            else
            {
                plan.Details = new Dictionary<string, string>
                {
                    ["macro"] = request.Macro,
                    ["layoutWarnings"] = string.Join("\n", request.LayoutWarnings),
                    ["deviceCount"] = inMacro.Count.ToString(CultureInfo.InvariantCulture),
                    ["devices"] = string.Join("\n", inMacro.Take(MacroInsertRequest.MaxDevices).Select(d => d.Tag + "\t" + d.VisibleTag + "\t" + d.Category)),
                    ["objectCount"] = objectCount.ToString(CultureInfo.InvariantCulture),
                    ["pageSizeKnown"] = request.Page.Width is null ? "false" : "true",
                };
            }
        }
        if (batch)
        {
            plan.Details = new Dictionary<string, string>
            {
                ["items"] = Protocol.BridgeJson.FromObject(items).ToString(Newtonsoft.Json.Formatting.None),
                ["layoutWarnings"] = string.Join("\n", requests.SelectMany(request => request.LayoutWarnings).Distinct(StringComparer.Ordinal)),
                ["deviceCount"] = items.Sum(item => item.DeviceCount).ToString(CultureInfo.InvariantCulture),
            };
        }
        return plan;
    }
}
