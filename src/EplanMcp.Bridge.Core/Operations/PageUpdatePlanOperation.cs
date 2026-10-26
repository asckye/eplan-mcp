using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageUpdatePlan"/>: finds the pages and returns one <c>update</c> change per page whose
/// description in the language differs, with the fingerprint (docs/tools/eplan_page_update.md, section 5). Reads only.
/// </summary>
public sealed class PageUpdatePlanOperation(IProjectCatalog catalog, IPageReader reader, IPageDescriptions descriptions, Func<IPagePlotFrames>? frames = null) : Operation<PageUpdateArgs, WritePlan>
{
    public override string Name => OperationNames.PageUpdatePlan;

    protected override WritePlan Execute(PageUpdateArgs args)
    {
        using IPagePlotFrames? session = args.Pages?.Any(p => p?.PlotFrame is not null) == true ? frames?.Invoke() : null;
        IPageDescriptions texts = session ?? descriptions;
        var request = PageUpdateRequest.Check(args, catalog, reader, texts, session);
        List<(PageUpdateRequest.Page Page, PageDescription Before)> pages = [.. request.Pages.Select(p =>
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            return (p, texts.Read(request.Project.Path, p.Name));
        })];
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes =
            [
                .. pages.Where(p => Changes(p, request.Language)).Select(p => new PlannedChange
                {
                    Action = "update",
                    Kind = "page",
                    Name = p.Page.Name,
                    Property = $"description ({request.Language})",
                    OldValue = p.Before.TextIn(request.Language),
                    NewValue = p.Page.Text,
                }),
                .. pages.Where(p => request.FrameChanges(p.Page)).Select(p => new PlannedChange
                {
                    Action = "update",
                    Kind = "page",
                    Name = p.Page.Name,
                    Property = "plot_frame",
                    OldValue = request.Frames[p.Page.Name].Effective,
                    NewValue = p.Page.PlotFrame,
                }),
            ],
            Fingerprint = request.FingerprintOf(pages),
        };
    }

    /// <summary>Whether the page's text in the language is not already the requested one.</summary>
    internal static bool Changes((PageUpdateRequest.Page Page, PageDescription Before) page, string language) =>
        page.Page.HasDescription && !string.Equals(page.Before.TextIn(language), page.Page.Text, StringComparison.Ordinal);
}
