using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageCreateExecute"/>: checks the arguments and the plan's fingerprint again, then creates
/// every page in one transaction and reads each one back; a name or description EPLAN did not keep rolls all of them
/// back (docs/tools/eplan_page_create.md, section 5; api-notes A13).
/// </summary>
public sealed class PageCreateExecuteOperation(IProjectCatalog catalog, IPageReader reader, IPageWriter writer, ITransactionScopeFactory transactions)
    : Operation<PageCreateArgs, PageCreateData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_page_create".</summary>
    public const string Tool = "eplan_page_create";

    public override string Name => OperationNames.PageCreateExecute;

    protected override PageCreateData Execute(PageCreateArgs args)
    {
        PageCreateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one page.create.plan returned.");
            }

            request = PageCreateRequest.Check(args, catalog, writer);
            IReadOnlyList<string> existing = reader.PageNames(request.Project.Path);
            if (!string.Equals(request.FingerprintOf(existing), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "Pages were added or removed where the new pages go after the plan was made, so nothing was created. Call eplan_page_create again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }

            request.CheckFree(existing);
        }
        catch (BridgeErrorException ex)
        {
            ex.Error.Execution = ExecutionState.NotStarted;
            throw;
        }

        Committed<List<PageSummary>> committed = TransactionRunner.Run(transactions, Tool, () => request.Pages.Select(page => Create(request, page)).ToList());
        return new PageCreateData { Project = request.Project.Name, Pages = committed.Result, Warning = committed.Warning };
    }

    private PageSummary Create(PageCreateRequest request, PageCreateRequest.Page page)
    {
        PageSummary created = writer.Create(request.Project.Path, page.Name, page.Type, page.Description, request.Language);
        if (!string.Equals(created.Name, page.Name.Text, StringComparison.Ordinal))
        {
            throw ReadBack(
                $"This project's page structure does not use every part of '{page.Name.Text}'; EPLAN would have named it '{created.Name}', so nothing was created. Use the parts that existing page names use (eplan_page_list).",
                page.Name.Text,
                created.Name);
        }

        if (page.Description is not null && !string.Equals(created.Description, page.Description, StringComparison.Ordinal))
        {
            throw ReadBack(
                $"EPLAN did not keep the description of '{page.Name.Text}' as given, so nothing was created. Leave out unusual characters and call again.",
                page.Description,
                created.Description ?? string.Empty);
        }

        return created;
    }

    private static BridgeErrorException ReadBack(string message, string requested, string actual) =>
        new(ErrorReason.InvalidArgument, message, retryable: false, new Dictionary<string, string> { ["requested"] = requested, ["actual"] = actual });
}
