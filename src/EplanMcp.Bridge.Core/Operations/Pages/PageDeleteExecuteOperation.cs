using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageDeleteExecute"/>: checks the arguments and the plan's fingerprint again, backs the project
/// up (<see cref="ProjectBackups"/>), then removes every page in one transaction and reads back; a page that is still
/// there, or a page count that did not fall by as many, rolls all of them back (docs/tools/eplan_page_delete.md,
/// section 5; spike S12, api-notes A18).
/// </summary>
public sealed class PageDeleteExecuteOperation(IProjectCatalog catalog, IPageReader pages, IPageRemover remover, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<PageDeleteArgs, PageDeleteData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_page_delete".</summary>
    public const string Tool = "eplan_page_delete";

    public override string Name => OperationNames.PageDeleteExecute;

    protected override PageDeleteData Execute(PageDeleteArgs args)
    {
        PageDeleteRequest request;
        PageDeletion deletion;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one page.delete.plan returned.");
            }

            request = PageDeleteRequest.Check(args, catalog);
            deletion = request.Read(remover);
            if (!string.Equals(deletion.Fingerprint, args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The pages, or what other pages keep of the devices on them, changed after the plan was made, so nothing was deleted. Call eplan_page_delete again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        int before = pages.PageNames(request.Project.Path).Count;
        Committed<List<OrphanedDevice>> committed = TransactionRunner.Run(transactions, Tool, () => Remove(request, deletion, before));
        return new PageDeleteData
        {
            Project = request.Project.Name,
            Pages = [.. deletion.Pages.Select((page, i) => new DeletedPage { Name = page.Name, Type = page.Type, Functions = deletion.FunctionCounts[i] })],
            Orphaned = [.. committed.Result.Take(PageDeleteRequest.MaxOrphaned)],
            OrphanedCount = committed.Result.Count,
            Warning = committed.Warning,
        };
    }

    /// <summary>Removes the pages, a safe point before each, and reads back; returns the devices left without a main function.</summary>
    private List<OrphanedDevice> Remove(PageDeleteRequest request, PageDeletion deletion, int before)
    {
        OperationContext context = OperationContext.Current;
        for (int i = 0; i < deletion.Pages.Count; i++)
        {
            context.ThrowIfCancellationRequested();
            context.ReportProgress(i, deletion.Pages.Count, "Deleting pages");
            remover.Remove(request.Project.Path, deletion.Pages[i].Name);
        }

        IReadOnlyList<string> names = pages.PageNames(request.Project.Path);
        string[] left = [.. deletion.Pages.Select(p => p.Name).Where(name => names.Contains(name, StringComparer.Ordinal))];
        if (left.Length > 0 || names.Count != before - deletion.Pages.Count)
        {
            throw new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN did not delete the pages as planned, so nothing was deleted. Ask the engineer to check the pages in EPLAN.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = request.Project.Name, ["notDeleted"] = string.Join("\n", left) });
        }

        return PageDeletion.Orphans(deletion.MainTags, remover.Functions(request.Project.Path));
    }
}
