using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

public sealed class PageReviewOperation(PageReviewOperation.IReader reader) : Operation<JObject, JObject>
{
    public interface IReader { public (ConnectionCreatePlanner.Scene Scene, JObject Actual) Read(string project, string page, JObject request); }
    public override string Name => Contracts.OperationNames.PageReview;
    protected override JObject Execute(JObject args)
    {
        if (string.IsNullOrWhiteSpace((string?)args["project"]) || string.IsNullOrWhiteSpace((string?)args["page"]))
        { throw InvalidArgument("/project and /page are required; use eplan_page_list."); }
        var (scene, actual) = reader.Read((string)args["project"]!, (string)args["page"]!, args);
        return new JObject { ["project"] = scene.Project.Name, ["page"] = scene.Page.Page, ["actual"] = actual, ["issues"] = PageReviewRules.Check(scene, actual, args) };
    }
}
