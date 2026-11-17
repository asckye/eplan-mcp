using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ReportUpdateExecute"/>: checks the arguments and the plan's fingerprint again, then updates
/// all reports, or the given report pages one by one, in one transaction, and reads the report pages back inside it to
/// list which were added and removed (docs/tools/eplan_report_update.md, section 5; api-notes A16). Pages are compared
/// by name: a report page updated in place is in neither list.
/// </summary>
public sealed class ReportUpdateExecuteOperation(IProjectCatalog catalog, IPageReader reader, IReportUpdater updater, ITransactionScopeFactory transactions)
    : Operation<ReportUpdateArgs, ReportUpdateData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_report_update".</summary>
    public const string Tool = "eplan_report_update";

    /// <summary>The most added and removed report pages the result lists together (tool spec 3).</summary>
    public const int MaxListed = 100;

    public override string Name => OperationNames.ReportUpdateExecute;

    protected override ReportUpdateData Execute(ReportUpdateArgs args)
    {
        ReportUpdateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one report.update.plan returned.");
            }

            request = ReportUpdateRequest.Check(args, catalog, reader, updater);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The project's report pages changed after the plan was made, so no report was updated. Call eplan_report_update again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<IReadOnlyList<ReportPageRecord>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            if (request.Pages is not { } pages)
            {
                // One EPLAN call: a cancel during it takes effect when it returns, as the rollback before the commit.
                updater.UpdateAll(request.Project.Path);
            }
            else
            {
                for (int i = 0; i < pages.Count; i++)
                {
                    string step = (i + 1).ToString(CultureInfo.InvariantCulture) + " of " + pages.Count.ToString(CultureInfo.InvariantCulture);
                    OperationContext.Current.ReportProgress(i, pages.Count, "Updating report page " + step);
                    OperationContext.Current.ThrowIfCancellationRequested();

                    // A page that updating an earlier page of its report removed is skipped; the comparison lists it.
                    _ = updater.Update(request.Project.Path, pages[i]);
                }
            }

            return updater.ReportPages(request.Project.Path);
        });
        return Compare(request, committed.Result, committed.Warning);
    }

    /// <summary>The report pages after that were not there before and the other way round, the removed ones listed first.</summary>
    private static ReportUpdateData Compare(ReportUpdateRequest request, IReadOnlyList<ReportPageRecord> after, string? warning)
    {
        var namesBefore = new HashSet<string>(request.ReportPages.Select(r => r.Name), StringComparer.Ordinal);
        var namesAfter = new HashSet<string>(after.Select(r => r.Name), StringComparer.Ordinal);
        string[] removed = [.. request.ReportPages.Select(r => r.Name).Where(name => !namesAfter.Contains(name))];
        string[] added = [.. after.Select(r => r.Name).Where(name => !namesBefore.Contains(name))];
        List<string> listedRemoved = [.. removed.Take(MaxListed)];
        return new ReportUpdateData
        {
            Project = request.Project.Name,
            Scope = request.Pages is null ? ReportUpdateData.ProjectScope : ReportUpdateData.PagesScope,
            Before = request.ReportPages.Count,
            After = after.Count,
            AddedCount = added.Length,
            RemovedCount = removed.Length,
            Removed = listedRemoved,
            Added = [.. added.Take(MaxListed - listedRemoved.Count)],
            Warning = warning,
        };
    }
}
