using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageDeletePlan"/>: finds the pages by their exact names and returns one <c>delete</c> change
/// per page, the devices the deletion leaves without a main function and the fingerprint
/// (docs/tools/eplan_page_delete.md, section 5). Reads only.
/// </summary>
public sealed class PageDeletePlanOperation(IProjectCatalog catalog, IPageRemover remover) : Operation<PageDeleteArgs, WritePlan>
{
    public override string Name => OperationNames.PageDeletePlan;

    protected override WritePlan Execute(PageDeleteArgs args)
    {
        PageDeleteRequest request = PageDeleteRequest.Check(args, catalog);
        PageDeletion deletion = request.Read(remover);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes =
            [
                .. deletion.Pages.Select((page, i) => new PlannedChange
                {
                    Action = "delete",
                    Kind = "page",
                    Name = page.Name,
                    OldValue = page.Type + ", " + Functions(deletion.FunctionCounts[i]),
                }),
            ],
            Fingerprint = deletion.Fingerprint,
            Details = new Dictionary<string, string>
            {
                // One line per page and per device, fields separated by tabs: the Host words them for the form and the result.
                ["pages"] = string.Join("\n", deletion.Pages.Select((page, i) => page.Name + "\t" + page.Type + "\t" + Number(deletion.FunctionCounts[i]))),
                ["orphaned"] = string.Join("\n", deletion.Orphaned.Take(PageDeleteRequest.MaxOrphaned).Select(Line)),
                ["orphanedCount"] = Number(deletion.Orphaned.Count),
            },
        };
    }

    /// <summary>A device left without its main function: tag, functions left, then the pages they are on.</summary>
    internal static string Line(OrphanedDevice device) => string.Join("\t", new[] { device.Tag, Number(device.FunctionsLeft) }.Concat(device.Pages));

    private static string Functions(int count) => Number(count) + (count == 1 ? " function" : " functions");

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
