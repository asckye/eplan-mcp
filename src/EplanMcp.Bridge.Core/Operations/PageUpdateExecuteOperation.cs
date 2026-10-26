using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageUpdateExecute"/>: checks the arguments and the plan's fingerprint again, then writes
/// every changing description in one transaction, keeping the other languages, and checks what EPLAN stored; any
/// difference rolls all of them back (docs/tools/eplan_page_update.md, section 5).
/// </summary>
public sealed class PageUpdateExecuteOperation(IProjectCatalog catalog, IPageReader reader, IPageDescriptions descriptions, ITransactionScopeFactory transactions, Func<IPagePlotFrames>? frames = null)
    : Operation<PageUpdateArgs, PageUpdateData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_page_update".</summary>
    public const string Tool = "eplan_page_update";

    public override string Name => OperationNames.PageUpdateExecute;

    protected override PageUpdateData Execute(PageUpdateArgs args)
    {
        using IPagePlotFrames? session = args.Pages?.Any(p => p?.PlotFrame is not null) == true ? frames?.Invoke() : null;
        IPageDescriptions texts = session ?? descriptions;
        PageUpdateRequest request;
        List<(PageUpdateRequest.Page Page, PageDescription Before)> pages;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one page.update.plan returned.");
            }

            request = PageUpdateRequest.Check(args, catalog, reader, texts, session);
            pages = [.. request.Pages.Select(p =>
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                return (p, texts.Read(request.Project.Path, p.Name));
            })];
            if (!string.Equals(request.FingerprintOf(pages), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    request.Frames.Count == 0 ? "These page descriptions changed after the plan was made, so nothing was changed. Call eplan_page_update again for a new plan."
                        : "These pages changed after the plan was made, so nothing was changed. Call eplan_page_update again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var changing = pages.Where(p => PageUpdatePlanOperation.Changes(p, request.Language) || request.FrameChanges(p.Page)).ToList();
        Committed<List<PageDescriptionChange>> committed = changing.Count == 0 ? new([], null)
            : TransactionRunner.Run(transactions, Tool, () => changing.Select(p => Write(request, p.Page, p.Before, texts, session)).ToList());
        return new PageUpdateData
        {
            Project = request.Project.Name,
            Language = request.Language,
            Pages = committed.Result,
            Unchanged = pages.Count - changing.Count,
            Warning = committed.Warning,
        };
    }

    private static PageDescriptionChange Write(PageUpdateRequest request, PageUpdateRequest.Page page, PageDescription before, IPageDescriptions descriptions, IPagePlotFrames? frames)
    {
        OperationContext.Current.ThrowIfCancellationRequested();
        PageDescription after = PageUpdatePlanOperation.Changes((page, before), request.Language)
            ? descriptions.Write(request.Project.Path, page.Name, PageDescriptionEdit.With(before.Entries, request.Language, page.Text)) : before;
        string? differs = page.HasDescription ? PageDescriptionEdit.Difference(before, after, request.Language, page.Text) : null;
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

        PagePlotFrameState? frame = null;
        if (page.PlotFrame is not null)
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            frame = request.FrameChanges(page) ? frames!.Write(request.Project.Path, page.Name, page.PlotFrame) : request.Frames[page.Name];
            if (frame.Identifier != request.Frames[page.Name].Identifier || frame.Effective != page.PlotFrame ||
                (request.FrameChanges(page) && frame.Setting != page.PlotFrame) || descriptions.Read(request.Project.Path, page.Name).Raw != after.Raw)
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                    "The frame setting, effective frame or description did not read back as planned; the entire batch was rolled back. Ask the engineer to check the page.",
                    metadata: new Dictionary<string, string> { ["page"] = page.Name });
            }
        }

        return new PageDescriptionChange
        {
            Name = page.Name,
            Description = page.HasDescription ? after.TextIn(request.Language) : null,
            Previous = page.HasDescription ? before.TextIn(request.Language) : null,
            PlotFrame = request.FrameChanges(page) ? frame?.Effective : null,
            PreviousPlotFrame = request.FrameChanges(page) ? request.Frames[page.Name].Effective : null,
        };
    }
}
