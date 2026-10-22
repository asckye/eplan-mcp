using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Backs up, rechecks the plan, imports and reads back in TransactionScope (S34, A39; tool spec 5).</summary>
public sealed class ImportListExecuteOperation(IProjectCatalog catalog, IPageReader pages, IListImporter importer, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<ImportListArgs, ImportListData>, IWriteOperation
{
    /// <summary>EPLAN's undo step description; these actions' undo after commit is unverified.</summary>
    public const string Tool = "eplan_import_list";

    private Action<ImportSource> ReadSource { get; } = source => source.Read(hash: true, maxBytes: 100 * 1024 * 1024);
    private Action<OpenProject, BackupTarget?> MakeBackup { get; } = (project, target) => ProjectBackups.Make(backup, pages, project, target);
    private Func<Func<TimeSpan>> StartClock { get; } = ProjectBackups.StartStopwatch;

    internal ImportListExecuteOperation(IProjectCatalog catalog, IPageReader pages, IListImporter importer, IProjectBackup backup, ITransactionScopeFactory transactions,
        Action<ImportSource> read, Action<OpenProject, BackupTarget?> makeBackup, Func<Func<TimeSpan>> startClock) : this(catalog, pages, importer, backup, transactions)
    {
        ReadSource = read;
        MakeBackup = makeBackup;
        StartClock = startClock;
    }

    public override string Name => OperationNames.ImportListExecute;

    protected override ImportListData Execute(ImportListArgs args)
    {
        Func<TimeSpan> elapsed = StartClock();
        ImportListRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one import.list.plan returned.");
            }

            OperationContext.Current.ThrowIfCancellationRequested();
            request = ImportListRequest.Check(args, catalog, importer, ReadSource);
            request.RequireFingerprint(args.Fingerprint);
            OperationContext.Current.ReportProgress(0, message: "Backing up project before list import");
            MakeBackup(request.Project, args.Backup);
            // Check both the file and the project again after the backup (tool spec 5).
            request = ImportListRequest.Check(args, catalog, importer, ReadSource);
            request.RequireFingerprint(args.Fingerprint);
            OperationContext.Current.ThrowIfCancellationRequested();
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<ListImportState> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            OperationContext context = OperationContext.Current;
            context.ThrowIfCancellationRequested();
            context.ReportProgress(1, message: "Importing project list");
            if (elapsed().TotalSeconds > ImportListArgs.ExecutionSeconds || !importer.Import(request.Project.Path, request.Kind, request.Source.FullPath, request.Converter))
            {
                throw Failed();
            }

            context.ThrowIfCancellationRequested();
            context.ReportProgress(2, message: "Reading back project list counts and references");
            ListImportState after = importer.Read(request.Project.Path);
            return elapsed().TotalSeconds > ImportListArgs.ExecutionSeconds ? throw Failed() : after;
        });
        return new ImportListData
        {
            Project = request.Project.Name,
            Kind = request.Kind,
            File = request.Source.File,
            Bytes = request.Source.Bytes,
            Sha256 = request.Source.Sha256,
            Converter = request.Converter,
            Before = request.State.Counts,
            After = committed.Result.Counts,
            Warning = "Only counts, device entry part numbers and readable part/reference fields were checked; row-level effects and undo after commit have not been verified. " + committed.Warning,
        };
    }

    private static BridgeErrorException Failed() => new(ErrorReason.EplanOperationFailed,
        "EPLAN refused the list import or its 300 s execution budget was exceeded. The import was rolled back; check the file and EPLAN messages. The project backup was kept.");
}
