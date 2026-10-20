using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageUpdateExecute"/>: checks the arguments and the plan's fingerprint again, then writes
/// every changing description in one transaction, keeping the other languages, and checks what EPLAN stored; any
/// difference rolls all of them back (docs/tools/eplan_page_update.md, section 5).
/// </summary>
public sealed class PageUpdateExecuteOperation(IProjectCatalog catalog, IPageReader reader, IPageDescriptions descriptions, ITransactionScopeFactory transactions)
    : Operation<PageUpdateArgs, PageUpdateData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_page_update".</summary>
    public const string Tool = "eplan_page_update";

    public override string Name => OperationNames.PageUpdateExecute;

    protected override PageUpdateData Execute(PageUpdateArgs args)
    {
        PageUpdateRequest request;
        List<(PageUpdateRequest.Page Page, PageDescription Before)> pages;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one page.update.plan returned.");
            }

            request = PageUpdateRequest.Check(args, catalog, reader, descriptions);
            pages = [.. request.Pages.Select(p => (p, descriptions.Read(request.Project.Path, p.Name)))];
            if (!string.Equals(PageUpdateRequest.FingerprintOf(pages), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "These page descriptions changed after the plan was made, so nothing was changed. Call eplan_page_update again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var changing = pages.Where(p => PageUpdatePlanOperation.Changes(p, request.Language)).ToList();
        Committed<List<PageDescriptionChange>> committed = TransactionRunner.Run(transactions, Tool, () => changing.Select(p => Write(request, p.Page, p.Before)).ToList());
        return new PageUpdateData
        {
            Project = request.Project.Name,
            Language = request.Language,
            Pages = committed.Result,
            Unchanged = pages.Count - changing.Count,
            Warning = committed.Warning,
        };
    }

    private PageDescriptionChange Write(PageUpdateRequest request, PageUpdateRequest.Page page, PageDescription before)
    {
        PageDescription after = descriptions.Write(request.Project.Path, page.Name, PageDescriptionEdit.With(before.Entries, request.Language, page.Text));
        string? differs = PageDescriptionEdit.Difference(before, after, request.Language, page.Text);
        if (differs is not null)
        {
            bool target = string.Equals(differs, request.Language, StringComparison.OrdinalIgnoreCase);
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"EPLAN did not store the description of '{page.Name}' as planned (its {differs} text differs), so nothing was changed. Leave out unusual characters and call again.",
                retryable: false,
                new Dictionary<string, string>
                {
                    ["page"] = page.Name,
                    ["language"] = differs,
                    ["requested"] = (target ? page.Text : before.TextIn(differs)) ?? string.Empty,
                    ["actual"] = after.TextIn(differs) ?? string.Empty,
                });
        }

        return new PageDescriptionChange { Name = page.Name, Description = after.TextIn(request.Language), Previous = before.TextIn(request.Language) };
    }
}
