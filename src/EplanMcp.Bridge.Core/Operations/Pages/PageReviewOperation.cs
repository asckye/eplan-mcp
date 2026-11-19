using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

public sealed class PageReviewOperation(PageReviewOperation.IReader reader) : Operation<JObject, JObject>
{
    public interface IReader { public (PageScene Scene, JObject Actual) Read(string project, string page, JObject request); }
    public override string Name => Contracts.OperationNames.PageReview;
    protected override JObject Execute(JObject args)
    {
        if (string.IsNullOrWhiteSpace((string?)args["project"]) || string.IsNullOrWhiteSpace((string?)args["page"]))
        { throw InvalidArgument("/project and /page are required; use eplan_page_list."); }
        var (scene, actual) = reader.Read((string)args["project"]!, (string)args["page"]!, args);
        var result = new JObject { ["project"] = scene.Project.Name, ["page"] = scene.Page.Page, ["actual"] = actual };
        scene = ApplyLayout(scene, args["layout"] as JObject, result);
        result["issues"] = PageReviewRules.Check(scene, actual, args);
        return result;
    }

    private static PageScene ApplyLayout(PageScene scene, JObject? request, JObject result)
    {
        var sources = new JObject();
        var warnings = new JObject();
        double? Read(string member, string key, bool positive = false)
        {
            sources[member] = scene.ConfigLayoutMembers.Contains(member) ? "config" : "default";
            if (request?[member] is not { } value)
            { return null; }
            if (value.Type is not (JTokenType.Integer or JTokenType.Float) || (double)value is not (>= 0 and <= 100)
                || positive && (double)value == 0)
            {
                warnings[member] = $"review.layout.{key} is outside the accepted numeric range.";
                return null;
            }
            double number = (double)value;
            if (member == "marginMm" && LayoutGuard.UsableArea(scene.Page, scene.Page.DrawingArea, number, true) is null)
            {
                warnings[member] = $"review.layout.{key} leaves no usable drawing area on this page.";
                return null;
            }
            sources[member] = "request";
            return number;
        }
        var layout = new LayoutGuard.Options
        {
            ParallelSpacingMm = Read("parallelSpacingMm", "parallel_spacing_mm") ?? scene.Layout.ParallelSpacingMm,
            MinimumTextHeightMm = Read("minimumTextHeightMm", "minimum_text_height_mm", positive: true) ?? scene.Layout.MinimumTextHeightMm,
            MarginMm = Read("marginMm", "margin_mm") ?? scene.Layout.MarginMm,
            SymbolClearanceMm = scene.Layout.SymbolClearanceMm,
            Strict = scene.Layout.Strict,
        };
        result["layout"] = new JObject
        {
            ["parallelSpacingMm"] = layout.ParallelSpacingMm,
            ["minimumTextHeightMm"] = layout.MinimumTextHeightMm,
            ["marginMm"] = layout.MarginMm,
            ["sources"] = sources,
        };
        if (warnings.HasValues)
        { result["layoutWarnings"] = warnings; }
        return scene.WithLayout(layout);
    }
}
