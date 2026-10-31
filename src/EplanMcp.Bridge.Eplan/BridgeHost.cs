using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Bridge.Eplan.Hosting;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan;

/// <summary>
/// Composition root of the Bridge: wires the EPLAN adapters into Bridge.Core and starts the pipe server, for the
/// add-in and for the offline host (architecture 5.4). Called on EPLAN's main thread only; <see cref="_server"/> is
/// the add-in's and has no other owner.
/// </summary>
internal static class BridgeHost
{
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(10);

    private static BridgeServer? _server;

    /// <summary>Where EPLAN registered the add-in, set by <see cref="EplanAddIn.OnBeforeInit"/> before the Bridge starts; null in the offline host.</summary>
    public static string? AddinPath { get; set; }

    /// <summary>The Bridge's version, as the handshake reports it.</summary>
    public static string Version =>
        typeof(BridgeHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>Main thread (OnInitGui). Kept out of line so a missing Bridge assembly fails here, inside the caller's catch.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Start()
    {
        if (_server is not null)
        {
            return;
        }

        var log = new FileBridgeLog(DataLocations.Logs);
        log.Write(BridgeLogLevel.Information, $"EplanMcp add-in {Version} starting");
        log.Write(BridgeLogLevel.Information, $"Registered from {AddinPath ?? "an unknown path"}");
        _server = Start(log, BridgeProtocol.PipePrefix, EplanEnvironment.Capture, AddinPath);
    }

    /// <summary>
    /// Starts a Bridge in this EPLAN process and returns its running server; the caller disposes it. Main thread, once
    /// EPLAN is up: the add-in's OnInitGui, or the offline host after <c>EplApplication.Init</c> (M5 design 4.9).
    /// <paramref name="describe"/> runs after the dispatcher is captured. Without a user interface
    /// (<see cref="EplanInstanceInfo.Offline"/>) there is no approval dialog, so approval.ask is not offered.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static BridgeServer Start(IBridgeLog log, string pipePrefix, Func<IBridgeLog, EplanInstanceInfo> describe, string? addinPath = null)
    {
        EplanDispatcher dispatcher = EplanDispatcher.CaptureOnMainThread();
        log.Write(BridgeLogLevel.Information, $"Main-thread dispatcher: the WPF dispatcher of thread {Environment.CurrentManagedThreadId}");

        EplanInstanceInfo eplan = describe(log);
        log.Write(BridgeLogLevel.Information, $"EPLAN {eplan.Version}, variant {eplan.Variant ?? "unknown"}, API extension licence {eplan.ApiExtensionLicence?.ToString() ?? "unknown"}{(eplan.Offline ? ", offline" : string.Empty)}");

        var options = new BridgeServerOptions
        {
            BridgeVersion = Version,
            Eplan = eplan,
            ApiExtensionLicence = () => dispatcher.Run(() => EplanEnvironment.ApiExtension(log), _startTimeout).Value as bool?,
            BuildTarget = typeof(BridgeHost).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
            AddinPath = addinPath,
        };
        var catalog = new EplanProjectCatalog();
        var pages = new EplanPageReader();
        var devices = new EplanDeviceReader();
        var terminals = new EplanTerminalReader();
        var macros = new EplanMacroReader();
        var pageWriter = new EplanPageWriter();
        var transactions = new EplanTransactions();
        var descriptions = new EplanPageDescriptions();
        var pageTexts = new EplanPageTexts();
        var renamer = new EplanDeviceRenamer();
        var generator = new EplanConnectionGenerator();
        var connectionCreator = new EplanConnectionCreator();
        var inserter = new EplanMacroInserter();
        var messages = new EplanProjectMessageReader();
        var checker = new EplanCheckRunner();
        var reports = new EplanReportUpdater();
        var exporter = new EplanExporter();
        var projectExporter = new EplanProjectExporter();
        var masterData = new EplanMasterDataExporter();
        var remover = new EplanPageRemover();
        var renumberer = new EplanDeviceRenumberer();
        var pageRenumberer = new EplanPageRenumberer();
        var terminalRenumberer = new EplanTerminalRenumberer();
        var cableRenumberer = new EplanCableRenumberer();
        var cableGenerator = new EplanCableGenerator();
        var connectionNumberer = new EplanConnectionNumberer();
        var backup = new EplanProjectBackup();
        var languages = new EplanProjectLanguages();
        var languageRemover = new EplanLanguageRemover();
        var partsDatabase = new EplanPartsDatabase();
        var placer = new EplanDevicePlacer();
        var drawings = new EplanDrawingImporter();
        var pdfComments = new EplanPdfCommentsImporter();
        var lists = new EplanListImporter();
        var plcImporter = new EplanPlcImporter();
        var translator = new EplanProjectTranslator();
        var placeholders = new EplanPlaceholderReader();
        var projectFiles = new EplanProjectFiles();
        var factory = new EplanProjectFactory(log);
        var actions = new EplanActionRunner();
        int actionMajor = System.Version.TryParse(eplan.Version, out System.Version? actionVersion) ? actionVersion.Major : 0;
        var stepGuard = new PlanStepGuardOperation(catalog, new EplanSnapshotReader(), new EplanObjectFinder(), new EplanPlanFunctionReader());
        var apiTrials = new EplanApiTryDispatcher();
        var apiPlan = new ApiTryPlanOperation(actionMajor, catalog, apiTrials);
        IOperation[] operations =
        [
            apiPlan,
            new ApiTryExecuteOperation(apiPlan, apiTrials, transactions),
            new ActionTryPlanOperation(catalog, actions),
            new ActionTryExecuteOperation(catalog, actions, new EplanSystemMessageReader(), transactions),
            new ActionRunPlanOperation(actionMajor, catalog, actions),
            new ActionRunOperation(actionMajor, catalog, actions, new EplanSystemMessageReader()),
            new ActionRunExecuteOperation(actionMajor, catalog, actions, new EplanSystemMessageReader(), transactions),
            new ProjectListOperation(catalog),
            new ProjectGetOperation(catalog, new EplanProjectReader()),
            new PropertyReadOperation(new EplanPropertyReadAdapter()),
            new ObjectReadOperation(catalog, new EplanObjectReader(), ObjectReadKind.Find),
            new ObjectReadOperation(catalog, new EplanObjectReader(), ObjectReadKind.Get),
            new PropertySetPlanOperation(path => new EplanPropertySetSession(path), actionMajor),
            new PropertySetExecuteOperation(path => new EplanPropertySetSession(path), transactions, actionMajor),
            new ProjectBackupOperation(catalog, pages, backup),
            new PageListOperation(catalog, pages),
            new PageGetOperation(catalog, pages),
            new PageOpenOperation(catalog, eplan.Offline ? null : new EplanPageEditor()),
            new PageDrawingGetOperation(catalog, new EplanDrawingReader()),
            new PlacementTextCreateExecuteOperation(catalog, new EplanPlacementTextWriter(), transactions, new EplanDrawingReader()),
            new DeviceListOperation(catalog, devices),
            new DeviceGetOperation(catalog, devices),
            new PlanFunctionsOperation(catalog, new EplanPlanFunctionReader()),
            new PlanNamesOperation(catalog, new EplanPlanFunctionReader()),
            stepGuard,
            new PlanStepExecuteOperation(stepGuard, catalog, pages, pageWriter, descriptions, devices, renamer,
                remover, pageRenumberer, renumberer, macros, inserter, backup, transactions),
            new ConnectionListOperation(catalog, devices, new EplanConnectionReader()),
            new CableGetOperation(catalog, devices, new EplanCableReader()),
            new TerminalStripListOperation(catalog, terminals),
            new TerminalStripGetOperation(catalog, terminals),
            new PlcGetOperation(catalog, devices, new EplanPlcReader()),
            new PartListOperation(catalog, new EplanPartReader()),
            new PartSearchOperation(new EplanPartsDatabase()),
            new RevisionListOperation(catalog, new EplanRevisionReader()),
            new SymbolSearchOperation(catalog, new EplanSymbolReader()),
            new PlaceholderListOperation(catalog, pages, placeholders),
            new MessageListOperation(new EplanSystemMessageReader()),
            new CheckListOperation(catalog, messages),
            new MacroListOperation(macros),
            new MacroGetOperation(catalog, macros),
            new PlanMacroInspectOperation(catalog, macros),
            .. eplan.Offline ? [] : new IOperation[] { new ApprovalAskOperation(new EplanApprovalDialog()) },
            new PageCreatePlanOperation(catalog, pages, pageWriter),
            new PageCreateExecuteOperation(catalog, pages, pageWriter, transactions),
            new PageUpdatePlanOperation(catalog, pages, descriptions, () => new EplanPagePlotFrames()),
            new PageUpdateExecuteOperation(catalog, pages, descriptions, transactions, () => new EplanPagePlotFrames()),
            new PagePlotFrameInspectOperation(catalog, () => new EplanPagePlotFrames()),
            new PageTextReplacePlanOperation(catalog, pageTexts),
            new PageTextReplaceExecuteOperation(catalog, pageTexts, transactions),
            new PageTextInspectOperation(catalog, pageTexts),
            new DeviceUpdatePlanOperation(catalog, devices, renamer),
            new DeviceUpdateExecuteOperation(catalog, devices, renamer, transactions),
            new DevicePlacePlanOperation(catalog, pages, renamer, partsDatabase, placer, new EplanSymbolReader(), new EplanPlcReader()),
            new DevicePlaceExecuteOperation(catalog, pages, renamer, partsDatabase, placer, transactions, new EplanSymbolReader(),
                (path, page, symbol) => new EplanDeviceSymbolPlacement(path, page, symbol), new EplanPlcReader()),
            new ConnectionGeneratePlanOperation(catalog, pages, generator),
            new ConnectionCreatePlanOperation(connectionCreator),
            new ConnectionCreateExecuteOperation(connectionCreator, generator, transactions, new EplanPlacementMover()),
            new ConnectionGenerateExecuteOperation(catalog, pages, generator, transactions),
            new MacroInsertPlanOperation(catalog, pages, devices, macros, inserter),
            new MacroInsertExecuteOperation(catalog, pages, devices, macros, inserter, transactions),
            new PlaceholderApplyPlanOperation(catalog, pages, placeholders),
            new PlaceholderApplyExecuteOperation(catalog, pages, placeholders, new EplanPlaceholderWriter(), transactions),
            new ImportDxfPlanOperation(catalog, pages, drawings),
            new ImportDxfExecuteOperation(catalog, pages, drawings, transactions),
            new ImportPdfCommentsPlanOperation(catalog, pdfComments),
            new ImportPdfCommentsExecuteOperation(catalog, pdfComments, transactions, new EplanSystemMessageReader()),
            new ImportListPlanOperation(catalog, lists),
            new ImportListExecuteOperation(catalog, pages, lists, backup, transactions, new EplanSystemMessageReader()),
            new ImportPlcPlanOperation(catalog, plcImporter),
            new ImportPlcExecuteOperation(catalog, pages, plcImporter, backup, transactions, new EplanSystemMessageReader()),
            new ImportPxfPlanOperation(),
            new ImportPxfExecuteOperation(new EplanProjectImporter()),
            new CheckRunPlanOperation(catalog, pages, checker, messages),
            new CheckRunExecuteOperation(catalog, pages, checker, messages, transactions),
            new ReportUpdatePlanOperation(catalog, pages, reports),
            new ReportUpdateExecuteOperation(catalog, pages, reports, transactions),
            .. new[] { ExportKind.Pdf, ExportKind.Dxf, ExportKind.Image }.SelectMany(kind => new IOperation[]
            {
                new ExportPlanOperation(kind, catalog, pages, exporter),
                new ExportRunOperation(kind, catalog, pages, exporter),
            }),
            .. new[] { ProjectExportKind.Pxf, ProjectExportKind.PartsList, ProjectExportKind.Labels, ProjectExportKind.Untranslated }.SelectMany(kind => new IOperation[]
            {
                new ProjectExportPlanOperation(kind, catalog, projectExporter),
                new ProjectExportRunOperation(kind, catalog, projectExporter),
            }),
            .. new[] { MasterDataExportKind.Parts, MasterDataExportKind.Translations }.SelectMany(kind => new IOperation[]
            {
                new MasterDataExportPlanOperation(kind, masterData),
                new MasterDataExportRunOperation(kind, masterData, new EplanMasterDataBackup()),
            }),
            new PageDeletePlanOperation(catalog, remover),
            new PageDeleteExecuteOperation(catalog, pages, remover, backup, transactions),
            new DeviceRenumberPlanOperation(catalog, renumberer),
            new DeviceRenumberExecuteOperation(catalog, pages, renumberer, backup, transactions),
            new PageRenumberPlanOperation(catalog, pages, pageRenumberer),
            new PageRenumberExecuteOperation(catalog, pages, pageRenumberer, backup, transactions),
            new LanguageSetPlanOperation(catalog, languages),
            new LanguageSetExecuteOperation(catalog, languages, transactions),
            new LanguageRemovePlanOperation(catalog, languages, languageRemover),
            new LanguageRemoveExecuteOperation(catalog, pages, languages, languageRemover, backup, transactions, log),
            new ProjectTranslatePlanOperation(catalog, pages, languages, translator),
            new ProjectTranslateExecuteOperation(catalog, pages, languages, translator, backup, transactions, log),
            new TerminalRenumberPlanOperation(catalog, terminalRenumberer),
            new TerminalRenumberExecuteOperation(catalog, pages, terminalRenumberer, backup, transactions),
            new CableRenumberPlanOperation(catalog, devices, cableRenumberer),
            new CableRenumberExecuteOperation(catalog, pages, devices, cableRenumberer, backup, transactions),
            new CableGeneratePlanOperation(catalog, cableGenerator),
            new CableGenerateExecuteOperation(catalog, pages, cableGenerator, backup, transactions, new EplanSystemMessageReader()),
            new ConnectionNumberPlanOperation(catalog, connectionNumberer),
            new ConnectionNumberExecuteOperation(catalog, pages, connectionNumberer, backup, transactions),
            new ProjectSnapshotOperation(catalog, new EplanSnapshotReader(), projectFiles),
            new ObjectResolveOperation(catalog, new EplanObjectFinder()),
            new ProjectCompatibilityOperation(catalog, projectFiles),
            new ProjectCreatePlanOperation(),
            new ProjectCreateExecuteOperation(factory, projectFiles),
            new ProjectRestorePlanOperation(),
            new ProjectRestoreExecuteOperation(factory, projectFiles),

            // Master-data writes always need the engineer's approval (ADR-0019), which the offline host cannot ask.
            .. eplan.Offline ? [] : MasterDataOperations(catalog, pages, backup, transactions),
        ];
        var router = new OperationRouter(operations, dispatcher, new EplanExceptionMapper(), log, _startTimeout);
        var pipes = new SecurePipeFactory(options.MaxConnections);
        var server = new BridgeServer(BridgeServer.CreatePipeName(pipePrefix, eplan.ProcessId), pipes.Create, options, router, log);
        server.Start();
        return server;
    }

    /// <summary>The operations of writes to system master data and their backup (ADR-0019).</summary>
    private static IOperation[] MasterDataOperations(IProjectCatalog catalog, IPageReader pages, IProjectBackup backup, ITransactionScopeFactory transactions)
    {
        var parts = new EplanPartsDatabaseWriter();
        var translations = new EplanTranslationDatabaseWriter();
        var reader = new EplanPartReader();
        return
        [
            new MasterDataBackupOperation(new EplanMasterDataBackup()),
            new PartImportPlanOperation(parts),
            new PartImportExecuteOperation(parts),
            new TranslationImportPlanOperation(translations),
            new TranslationImportExecuteOperation(translations),
            new PartSyncPlanOperation(catalog, reader, parts),
            new PartSyncExecuteOperation(catalog, pages, reader, parts, backup, transactions),
        ];
    }

    /// <summary>Main thread (OnExit). Does not wait for the listeners (see <see cref="BridgeServer.Dispose"/>).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Stop()
    {
        _server?.Dispose();
        _server = null;
    }
}
