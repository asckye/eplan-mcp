using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Explicit editor navigation; null editor means an offline host with no UI.</summary>
public sealed class PageOpenOperation(IProjectCatalog catalog, IPageEditor? editor) : Operation<PageOpenArgs, PageOpenData>
{
    public override string Name => OperationNames.PageOpen;

    public override object ParseArgs(JToken? args)
    {
        if (args is JObject obj && obj.Properties().FirstOrDefault(p => p.Name is not ("project" or "page")) is { } unknown)
        {
            throw InvalidArgument("/" + unknown.Name.Replace("~", "~0").Replace("/", "~1") + " is unknown; omit this member.");
        }
        if (args is JObject values && values.Properties().FirstOrDefault(p => p.Value.Type != JTokenType.String) is { } invalid)
        { throw InvalidArgument("/" + invalid.Name + " must be a string."); }
        return base.ParseArgs(args);
    }

    protected override PageOpenData Execute(PageOpenArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Project))
        { throw InvalidArgument("/project is required; choose an open project from eplan_project_list."); }
        if (string.IsNullOrWhiteSpace(args.Page))
        { throw InvalidArgument("/page is required; use the exact full name from eplan_page_list."); }
        if (editor is null)
        {
            throw new BridgeErrorException(ErrorReason.CapabilityMissing,
                "The offline host has no page editor. Connect to the EplanMcp add-in in an EPLAN window.",
                retryable: false, new Dictionary<string, string> { ["missing"] = Name });
        }
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string page = editor.Open(project.Path, args.Page)
            ?? throw new BridgeErrorException(ErrorReason.PageNotFound,
                "No page has this exact name in the specified project. Use eplan_page_list to find its full name.",
                retryable: false, new Dictionary<string, string> { ["project"] = project.Name, ["page"] = args.Page });
        return new PageOpenData { Project = project.Name, Page = page, EditorOpened = true };
    }
}
