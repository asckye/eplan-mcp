using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;

internal static class BlockCreation
{
    internal static PlacementTextCreateData Run(PlacementTextCreateArgs args, IProjectCatalog catalog, IPageDrawingReader? reader,
        IPlacementTextWriter writer, ITransactionScopeFactory transactions, bool planning)
    {
        void Invalid(string message) => throw WriteArguments.NotStarted(WriteArguments.Invalid([message]));
        if (string.IsNullOrWhiteSpace(args.Project) || string.IsNullOrWhiteSpace(args.Page))
        { Invalid("Specify an explicit project and page."); }
        List<PlacementText> items = args.Items ?? [args.Text];
        if (items.Count != 1 || items[0].Kind != "block")
        { Invalid("A block must be the only item in its call."); }
        PlacementText block = items[0];
        if (block.Members is not { Count: >= 2 and <= 1000 })
        { Invalid("/members requires 2..1000 drawing locators."); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        PageDrawingGeometry? page = reader?.Read(project.Path, args.Page);
        if (page is null)
        { throw WriteArguments.NotStarted(new(ErrorReason.PageNotFound, "Re-read the target page before grouping.")); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DrawingLocator member in block.Members!)
        {
            if (member is null || !ProjectResolver.SamePath(member.Project, project.Path) || member.Page != args.Page)
            { Invalid("All block members must belong to the requested project and page."); }
            if (!seen.Add(member!.Type + "/" + member.Oid))
            { Invalid("Block members must be distinct."); }
            DrawingPlacement row = page.Placements.FirstOrDefault(p => DrawingLocators.Matches(member, p.Locator))
                ?? throw WriteArguments.NotStarted(new(ErrorReason.PlanStale, "A member locator is stale; call eplan_page_get with include_drawings again."));
            if (row.Parent is not null || row.Properties.TryGetValue("grouped", out string? grouped) && grouped == "true")
            { Invalid("A block member already belongs to a block, group or property owner."); }
        }
        writer.Validate(project.Path, args.Page, block);
        if (planning)
        { return new() { Text = block, Items = items }; }
        Committed<List<PlacementText>> result = TransactionRunner.Run(transactions, "eplan_placement_create", () =>
        {
            List<PlacementText> actual = writer.CreateBatch(project.Path, args.Page, items);
            if (actual.Count != 1 || actual[0].Kind != "block" || actual[0].Count != block.Members.Count || block.Name is not null && actual[0].Name != block.Name)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Block Count or Name did not read back; grouping was rolled back."); }
            return actual;
        });
        return new() { Text = result.Result[0], Items = result.Result, Warning = result.Warning };
    }
}
