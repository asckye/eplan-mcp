using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.CheckRunPlan"/>: finds the pages and counts the project's check messages, which the check
/// will replace; one <c>update</c> change of the project's <c>check messages</c>, whose new value only EPLAN can tell
/// (docs/tools/eplan_check_run.md, section 5). The details give the page count and the scheme for the approval form.
/// The scheme is not checked here: where EPLAN keeps the check schemes is not verified (M4 design 1), so a scheme EPLAN
/// does not know is found when the check runs. Reads only.
/// </summary>
public sealed class CheckRunPlanOperation(IProjectCatalog catalog, IPageReader reader, ICheckRunner runner, IProjectMessageReader messages) : Operation<CheckRunArgs, WritePlan>
{
    /// <summary>The <see cref="WritePlan.Details"/> key of how many pages are checked.</summary>
    public const string PageCountDetail = "pageCount";

    /// <summary>The <see cref="WritePlan.Details"/> key of the scheme as given; missing for EPLAN's current scheme.</summary>
    public const string SchemeDetail = "scheme";

    public override string Name => OperationNames.CheckRunPlan;

    protected override WritePlan Execute(CheckRunArgs args)
    {
        var request = CheckRunRequest.Check(args, catalog, reader);
        (IReadOnlyList<string> pageIds, int before) = request.ReadState(runner, messages);
        var details = new Dictionary<string, string> { [PageCountDetail] = pageIds.Count.ToString(CultureInfo.InvariantCulture) };
        if (request.Scheme is { } scheme)
        {
            details[SchemeDetail] = scheme;
        }

        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes =
            [
                new PlannedChange
                {
                    Action = "update",
                    Kind = "project",
                    Name = request.Project.Name,
                    Property = "check messages",
                    OldValue = before.ToString(CultureInfo.InvariantCulture),
                },
            ],
            Fingerprint = CheckRunRequest.StateFingerprint(pageIds, before),
            Details = details,
        };
    }
}
