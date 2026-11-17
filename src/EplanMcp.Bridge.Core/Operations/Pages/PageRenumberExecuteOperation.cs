using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageRenumberExecute"/>: checks the arguments, the pages and the plan's fingerprint again,
/// backs the project up (<see cref="ProjectBackups"/>), then lets EPLAN renumber the pages in one transaction and reads
/// back; two pages with one name, ignoring letter case, roll it all back (ALREADY_EXISTS), whether EPLAN made them or
/// refused (docs/tools/eplan_page_renumber.md, section 5; spike S12, api-notes A18).
/// </summary>
public sealed class PageRenumberExecuteOperation(IProjectCatalog catalog, IPageReader pages, IPageRenumberer renumberer, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<PageRenumberArgs, PageRenumberData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_page_renumber".</summary>
    public const string Tool = "eplan_page_renumber";

    public override string Name => OperationNames.PageRenumberExecute;

    protected override PageRenumberData Execute(PageRenumberArgs args)
    {
        PageRenumberRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one page.renumber.plan returned.");
            }

            request = PageRenumberRequest.Check(args, catalog);
            if (!string.Equals(request.FingerprintOf(renumberer, pages), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The pages, or the page names in their structures, changed after the plan was made, so nothing was renumbered. Call eplan_page_renumber again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        Committed<List<RenamedObject>> committed = TransactionRunner.Run(transactions, Tool, () => Renumber(request));
        return new PageRenumberData
        {
            Project = request.Project.Name,
            Changed = committed.Result,
            Unchanged = request.Pages.Count - committed.Result.Count,
            Warning = committed.Warning,
        };
    }

    /// <summary>Renumbers, then checks that every page name in the project is unique ignoring letter case.</summary>
    private List<RenamedObject> Renumber(PageRenumberRequest request)
    {
        IReadOnlyList<string> renamed = renumberer.Renumber(request.Project.Path, request.Pages, request.Start, request.Step, request.KeepText, request.Subpages);
        string[] taken = [.. pages.PageNames(request.Project.Path).GroupBy(name => name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)];
        if (taken.Length > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                "The new page numbers would give two pages the same name (letter case does not count), so nothing was renumbered. Choose another 'start'; eplan_page_list with name_prefix shows the numbers in use.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = request.Project.Name, ["existing"] = string.Join("\n", taken.Take(ProjectResolver.MaxCandidates)) });
        }

        return [.. request.Pages.Select((page, i) => new RenamedObject { From = page, To = renamed[i] }).Where(c => !string.Equals(c.From, c.To, StringComparison.Ordinal))];
    }
}
