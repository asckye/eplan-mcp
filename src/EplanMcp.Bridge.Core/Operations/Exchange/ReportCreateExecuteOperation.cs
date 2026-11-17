using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Creates report pages in one undo step; only managed snapshots cross the commit (A84, A79).</summary>
public sealed class ReportCreateExecuteOperation(IProjectCatalog catalog, IPageReader reader, IReportCreator creator, ITransactionScopeFactory transactions)
    : Operation<ReportCreateArgs, ReportCreateData>, IWriteOperation
{
    public override string Name => OperationNames.ReportCreateExecute;
    protected override ReportCreateData Execute(ReportCreateArgs args)
    {
        WritePlan plan;
        try
        {
            plan = (WritePlan)new ReportCreatePlanOperation(catalog, reader, creator).Execute(args)!;
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' from report.create.plan is required."); }
            if (!string.Equals(plan.Fingerprint, args.Fingerprint, StringComparison.Ordinal))
            { throw new BridgeErrorException(ErrorReason.PlanStale, "The page inventory changed; call eplan_report_create again for a new plan."); }
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        var before = new HashSet<string>(reader.PageNames(plan.ProjectPath), StringComparer.Ordinal);
        var data = new ReportCreateData();
        void ReadBack()
        {
            IReadOnlyList<ReportPageRecord> after = creator.ReportPages(plan.ProjectPath);
            data.Pages = [.. after.Where(p => p.Type == args.Type && p.Form == args.Form).Select(p => p.Name)];
            data.NewPages = [.. after.Select(p => p.Name).Where(p => !before.Contains(p))];
            if (data.NewPages.Count == 0 && data.Pages.Count == 0)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "No matching report pages were generated; check the report type, form and schemes."); }
        }
        Committed<ReportCreateData> committed = TransactionRunner.Run(transactions, "eplan_report_create", () =>
        { creator.Create(plan.ProjectPath, args); ReadBack(); return data; }, verifyCommitted: _ => ReadBack(),
            verifyRollback: () =>
            {
                if (((WritePlan)new ReportCreatePlanOperation(catalog, reader, creator).Execute(args)!).Fingerprint != plan.Fingerprint)
                { throw new InvalidOperationException("Report rollback did not restore the page inventory."); }
            });
        data.Warning = committed.Warning;
        return data;
    }
}
