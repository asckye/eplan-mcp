using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PlaceholderApplyPlan"/> (docs/tools/eplan_placeholder_apply.md, section 5): finds the
/// placeholder and returns one change, the value set, with the values it applies in the details, and the fingerprint.
/// EPLAN does not tell which value set is applied now, so the change has no old value; the current values are not read,
/// as EPLAN gives them only inside the write's transaction (api-notes A33). Reads only.
/// </summary>
public sealed class PlaceholderApplyPlanOperation(IProjectCatalog catalog, IPageReader pages, IPlaceholderReader reader) : Operation<PlaceholderApplyArgs, WritePlan>
{
    public override string Name => OperationNames.PlaceholderApplyPlan;

    protected override WritePlan Execute(PlaceholderApplyArgs args)
    {
        var request = PlaceholderApplyRequest.Check(args, catalog, pages, reader);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [new PlannedChange { Action = "update", Kind = "placeholder", Name = request.DisplayName, Property = "value set", NewValue = request.ValueSet }],
            Fingerprint = request.Fingerprint(),
            Details = new Dictionary<string, string>
            {
                ["page"] = request.Page,
                ["placeholder"] = request.Target.Name,
                ["values"] = string.Join("\n", request.Values().Select(v => Line(v.Variable) + "\t" + Line(v.Value.Text ?? string.Empty))),
                ["assignedObjects"] = request.Target.AssignedObjects.ToString(CultureInfo.InvariantCulture),
            },
        };
    }

    /// <summary>A text on one line of the details: tabs and line breaks become spaces, as the lines and fields are split on them.</summary>
    internal static string Line(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
