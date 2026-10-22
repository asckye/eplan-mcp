namespace EplanMcp.Contracts;

/// <summary>
/// Names of Bridge operations carried by <c>invoke</c>, formatted <c>&lt;resource&gt;.&lt;action&gt;</c>.
/// New capabilities add operations here, not new JSON-RPC methods (ADR-0003).
/// </summary>
public static class OperationNames
{
    /// <summary>Lists the projects open in EPLAN. Arguments: <see cref="ProjectListArgs"/>; data: <see cref="ProjectListData"/>.</summary>
    public const string ProjectList = "project.list";

    /// <summary>Overview of one project: counts, pages per type, optionally its properties. Arguments: <see cref="ProjectGetArgs"/>; data: <see cref="ProjectGetData"/>.</summary>
    public const string ProjectGet = "project.get";

    /// <summary>Pages of one project, filtered and paged in the Bridge. Arguments: <see cref="PageListArgs"/>; data: <see cref="ListData{TItem}"/> of <see cref="PageSummary"/>.</summary>
    public const string PageList = "page.list";

    /// <summary>One page by its full name: type, description, devices, optionally its properties. Arguments: <see cref="PageGetArgs"/>; data: <see cref="PageGetData"/>.</summary>
    public const string PageGet = "page.get";

    /// <summary>
    /// Lists the devices of a project, filtered and sliced in the Bridge. Arguments: <see cref="DeviceListArgs"/>;
    /// data: <see cref="ListData{TItem}"/> of <see cref="DeviceListItem"/>.
    /// </summary>
    public const string DeviceList = "device.list";

    /// <summary>One device by tag. Arguments: <see cref="DeviceGetArgs"/>; data: <see cref="DeviceGetData"/>.</summary>
    public const string DeviceGet = "device.get";

    /// <summary>
    /// Lists the connections of a device, of a page, or of both, sliced in the Bridge. Arguments:
    /// <see cref="ConnectionListArgs"/>; data: <see cref="ListData{TItem}"/> of <see cref="ConnectionListItem"/>.
    /// </summary>
    public const string ConnectionList = "connection.list";

    /// <summary>One cable by tag: type, ends, parts, and its cores sliced in the Bridge. Arguments: <see cref="CableGetArgs"/>; data: <see cref="CableGetData"/>.</summary>
    public const string CableGet = "cable.get";

    /// <summary>The terminal strips of a project with their counts, filtered, sorted and sliced in the Bridge. Arguments: <see cref="TerminalStripListArgs"/>; data: <see cref="ListData{TItem}"/> of <see cref="TerminalStripListItem"/>.</summary>
    public const string TerminalStripList = "terminal.strip.list";

    /// <summary>One terminal strip by name: its terminals in strip order, sliced in the Bridge, with their connections and bridges. Arguments: <see cref="TerminalStripGetArgs"/>; data: <see cref="TerminalStripGetData"/>.</summary>
    public const string TerminalStripGet = "terminal.strip.get";

    /// <summary>One PLC box by tag: station, rack, slot, and its channels with addresses and connections, sliced in the Bridge. Arguments: <see cref="PlcGetArgs"/>; data: <see cref="PlcGetData"/>.</summary>
    public const string PlcGet = "plc.get";

    /// <summary>
    /// Lists the parts a project uses, summed up per part number and variant, filtered and sliced in the Bridge.
    /// Arguments: <see cref="PartListArgs"/>; data: <see cref="ListData{TItem}"/> of <see cref="PartListItem"/>.
    /// </summary>
    public const string PartList = "part.list";

    /// <summary>
    /// Searches the parts database EPLAN is configured with, opened and closed again in the call, never changed:
    /// filtered, sorted and sliced in the Bridge. Arguments: <see cref="PartSearchArgs"/>; data: <see cref="PartSearchData"/>.
    /// </summary>
    public const string PartSearch = "part.search";

    /// <summary>
    /// A project's revision state by its link file's extension, its revision log sliced in the Bridge, and the pages
    /// EPLAN lists as changed; never changes a revision. Arguments: <see cref="RevisionListArgs"/>; data:
    /// <see cref="RevisionListData"/>.
    /// </summary>
    public const string RevisionList = "revision.list";

    /// <summary>
    /// EPLAN's system messages of this session, newest first, filtered and sliced in the Bridge. Arguments:
    /// <see cref="MessageListArgs"/>; data: <see cref="MessageListData"/>.
    /// </summary>
    public const string MessageList = "message.list";

    /// <summary>
    /// The check messages stored in a project (the last check's results), filtered and sliced in the Bridge; never
    /// runs a check. Arguments: <see cref="CheckListArgs"/>; data: <see cref="CheckListData"/>.
    /// </summary>
    public const string CheckList = "check.list";

    /// <summary>
    /// The window macros below EPLAN's macro directory, filtered by path and sliced in the Bridge; never opens a macro.
    /// Arguments: <see cref="MacroListArgs"/>; data: <see cref="MacroListData"/>.
    /// </summary>
    public const string MacroList = "macro.list";

    /// <summary>
    /// One window macro below the macro directory, opened in a project without inserting it: representations, variants,
    /// size and functions. Arguments: <see cref="MacroGetArgs"/>; data: <see cref="MacroGetData"/>.
    /// </summary>
    public const string MacroGet = "macro.get";

    /// <summary>
    /// The plan of eplan_page_create: checks the names and that they are free, never writes. Arguments:
    /// <see cref="PageCreateArgs"/>; data: <see cref="WritePlan"/>.
    /// </summary>
    public const string PageCreatePlan = "page.create.plan";

    /// <summary>
    /// Creates the planned pages in one transaction, after checking the plan's fingerprint. Arguments:
    /// <see cref="PageCreateArgs"/> with the fingerprint; data: <see cref="PageCreateData"/>.
    /// </summary>
    public const string PageCreateExecute = "page.create.execute";

    /// <summary>
    /// The plan of eplan_page_update: finds the pages and lists the descriptions that change, never writes. Arguments:
    /// <see cref="PageUpdateArgs"/>; data: <see cref="WritePlan"/>.
    /// </summary>
    public const string PageUpdatePlan = "page.update.plan";

    /// <summary>
    /// Changes the planned descriptions in one language in one transaction, keeping the other languages. Arguments:
    /// <see cref="PageUpdateArgs"/> with the fingerprint; data: <see cref="PageUpdateData"/>.
    /// </summary>
    public const string PageUpdateExecute = "page.update.execute";

    /// <summary>
    /// The plan of eplan_device_update: finds the device and the functions to rename, checks the new tag is free, never
    /// writes. Arguments: <see cref="DeviceUpdateArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string DeviceUpdatePlan = "device.update.plan";

    /// <summary>
    /// Renames the device's main and auxiliary functions in one transaction. Arguments: <see cref="DeviceUpdateArgs"/>
    /// with the fingerprint; data: <see cref="DeviceUpdateData"/>.
    /// </summary>
    public const string DeviceUpdateExecute = "device.update.execute";

    /// <summary>
    /// The plan of eplan_device_place: checks the page, the point, the part and its variant in the parts database, and
    /// that the tag is free, never writes. Arguments: <see cref="DevicePlaceArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string DevicePlacePlan = "device.place.plan";

    /// <summary>
    /// Places the part's device on the page and gives all its functions the tag, in one transaction, then reads back.
    /// Arguments: <see cref="DevicePlaceArgs"/> with the fingerprint; data: <see cref="DevicePlaceData"/>.
    /// </summary>
    public const string DevicePlaceExecute = "device.place.execute";

    /// <summary>
    /// The plan of eplan_connection_generate: finds the pages and counts their connections, never writes. Arguments:
    /// <see cref="ConnectionGenerateArgs"/>; data: <see cref="WritePlan"/>.
    /// </summary>
    public const string ConnectionGeneratePlan = "connection.generate.plan";

    /// <summary>
    /// Generates connections on the planned pages in one transaction, updating only. Arguments:
    /// <see cref="ConnectionGenerateArgs"/> with the fingerprint; data: <see cref="ConnectionGenerateData"/>.
    /// </summary>
    public const string ConnectionGenerateExecute = "connection.generate.execute";

    /// <summary>
    /// The plan of eplan_macro_insert: checks the page, the macro's path and content and where its outline goes, never
    /// writes. Arguments: <see cref="MacroInsertArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string MacroInsertPlan = "macro.insert.plan";

    /// <summary>
    /// Inserts the window macro on the page in one transaction and reads the inserted devices back. Arguments:
    /// <see cref="MacroInsertArgs"/> with the fingerprint; data: <see cref="MacroInsertData"/>.
    /// </summary>
    public const string MacroInsertExecute = "macro.insert.execute";

    /// <summary>
    /// The plan of eplan_import_dxf: finds the page and reads the file the Host resolved below its import root again,
    /// never writes. Arguments: <see cref="ImportDxfArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string ImportDxfPlan = "import.dxf.plan";

    /// <summary>
    /// Imports the drawing onto the page in one transaction and counts the objects it added. Arguments:
    /// <see cref="ImportDxfArgs"/> with the fingerprint; data: <see cref="ImportDxfData"/>.
    /// </summary>
    public const string ImportDxfExecute = "import.dxf.execute";

    /// <summary>
    /// The plan of eplan_import_pxf: checks the EPJ file the Host resolved below its import root and that the scratch
    /// folder has no project of the name, never writes; no open project is involved. Arguments: <see cref="ImportPxfArgs"/>;
    /// data: <see cref="WritePlan"/> whose project path is the new project's.
    /// </summary>
    public const string ImportPxfPlan = "import.pxf.plan";

    /// <summary>
    /// Lets EPLAN import the EPJ file as a new project at the planned path, without opening it, and checks its files; on
    /// any failure deletes the files the import made. Arguments: <see cref="ImportPxfArgs"/> with the fingerprint; data:
    /// <see cref="ImportPxfData"/>.
    /// </summary>
    public const string ImportPxfExecute = "import.pxf.execute";

    /// <summary>
    /// The plan of eplan_check_run: finds the pages and counts the project's check messages, never checks. Arguments:
    /// <see cref="CheckRunArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string CheckRunPlan = "check.run.plan";

    /// <summary>
    /// Runs EPLAN's check on the planned pages or the whole project in one transaction, which replaces the project's
    /// check messages, and reads them back. Arguments: <see cref="CheckRunArgs"/> with the fingerprint; data:
    /// <see cref="CheckRunData"/>.
    /// </summary>
    public const string CheckRunExecute = "check.run.execute";

    /// <summary>
    /// The plan of eplan_report_update: finds the report pages to update, or counts all of them, never updates.
    /// Arguments: <see cref="ReportUpdateArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string ReportUpdatePlan = "report.update.plan";

    /// <summary>
    /// Updates all reports of the project, or the planned report pages one by one, in one transaction, and compares the
    /// report pages before and after. Arguments: <see cref="ReportUpdateArgs"/> with the fingerprint; data:
    /// <see cref="ReportUpdateData"/>.
    /// </summary>
    public const string ReportUpdateExecute = "report.update.execute";

    /// <summary>
    /// The plan of eplan_export_pdf: finds the project and the pages (all when none are given) and checks the scheme,
    /// never exports. Arguments: <see cref="ExportArgs"/>; data: <see cref="ExportPlanData"/>.
    /// </summary>
    public const string ExportPdfPlan = "export.pdf.plan";

    /// <summary>
    /// Exports the pages as one PDF file into the Host's new, empty folder, then sets EPLAN's last used scheme back if
    /// the export changed it. Arguments: <see cref="ExportArgs"/> with the folder and file name; data: <see cref="ExportRunData"/>.
    /// </summary>
    public const string ExportPdfRun = "export.pdf.run";

    /// <summary>The plan of eplan_export_dxf, as <see cref="ExportPdfPlan"/>. Arguments: <see cref="ExportArgs"/>; data: <see cref="ExportPlanData"/>.</summary>
    public const string ExportDxfPlan = "export.dxf.plan";

    /// <summary>
    /// Exports the pages as DXF or DWG files, one per page, into the Host's new folder; the scheme may add subfolders.
    /// Arguments: <see cref="ExportArgs"/> with the folder; data: <see cref="ExportRunData"/>.
    /// </summary>
    public const string ExportDxfRun = "export.dxf.run";

    /// <summary>The plan of eplan_export_image: finds the project and 1 to 20 pages. Arguments: <see cref="ExportArgs"/>; data: <see cref="ExportPlanData"/>.</summary>
    public const string ExportImagePlan = "export.image.plan";

    /// <summary>
    /// Exports each page as one image into the Host's new folder, reporting progress and stopping at a cancel before each
    /// page. Arguments: <see cref="ExportArgs"/> with the folder; data: <see cref="ExportRunData"/> with the page of each file.
    /// </summary>
    public const string ExportImageRun = "export.image.run";

    /// <summary>The plan of eplan_export_pxf: finds the project, never exports. Arguments: <see cref="ExportArgs"/>; data: <see cref="ExportPlanData"/>.</summary>
    public const string ExportPxfPlan = "export.pxf.plan";

    /// <summary>
    /// Exports the whole project, connections included, as one EPJ (PXF) file into the Host's new, empty folder; EPLAN
    /// adds the extension. Arguments: <see cref="ExportArgs"/> with the folder, the file name without extension and
    /// <see cref="ExportArgs.IncludeMasterData"/>; data: <see cref="ExportRunData"/>.
    /// </summary>
    public const string ExportPxfRun = "export.pxf.run";

    /// <summary>The plan of eplan_export_parts: finds the project, never exports. Arguments: <see cref="ExportArgs"/>; data: <see cref="ExportPlanData"/>.</summary>
    public const string ExportPartsPlan = "export.parts.plan";

    /// <summary>
    /// Exports the project's parts list as one XML file into the Host's new, empty folder. Arguments:
    /// <see cref="ExportArgs"/> with the folder and the file name, <c>.xml</c> included; data: <see cref="ExportRunData"/>.
    /// </summary>
    public const string ExportPartsRun = "export.parts.run";

    /// <summary>
    /// The plan of eplan_export_labels: finds the project, the labelling scheme and its output format, and checks the
    /// language, never exports. Arguments: <see cref="ExportArgs"/>; data: <see cref="ExportPlanData"/> with the extension.
    /// </summary>
    public const string ExportLabelsPlan = "export.labels.plan";

    /// <summary>
    /// Exports labels of the project with a labelling scheme as one file into the Host's new, empty folder. Arguments:
    /// <see cref="ExportArgs"/> with the folder and the file name, the plan's extension included; data: <see cref="ExportRunData"/>.
    /// </summary>
    public const string ExportLabelsRun = "export.labels.run";

    /// <summary>
    /// The plan of eplan_export_untranslated: finds the project and checks the languages are its translation languages,
    /// never exports. Arguments: <see cref="ExportArgs"/>; data: <see cref="ExportPlanData"/>.
    /// </summary>
    public const string ExportUntranslatedPlan = "export.untranslated.plan";

    /// <summary>
    /// Exports the project's texts that the translation database has no translation for, in the given languages, as one
    /// tab-separated text file into the Host's new, empty folder. Arguments: <see cref="ExportArgs"/> with the folder and
    /// the file name, <c>.txt</c> included; data: <see cref="ExportRunData"/> with the languages.
    /// </summary>
    public const string ExportUntranslatedRun = "export.untranslated.run";

    /// <summary>
    /// The plan of eplan_part_export: opens the configured parts database, checks the given part numbers are in it and
    /// closes it again, never exports. Arguments: <see cref="ExportArgs"/>; data: <see cref="ExportPlanData"/> with the database.
    /// </summary>
    public const string PartExportPlan = "part.export.plan";

    /// <summary>
    /// Exports the given parts, every variant of each, or every part of the configured parts database as one XML or EDZ
    /// file into the Host's new, empty folder. Arguments: <see cref="ExportArgs"/> with the folder and the file name, its
    /// extension included; data: <see cref="ExportRunData"/> with the database and the part count.
    /// </summary>
    public const string PartExportRun = "part.export.run";

    /// <summary>
    /// The plan of eplan_translation_export: names the configured translation database, never exports. Arguments:
    /// <see cref="ExportArgs"/>; data: <see cref="ExportPlanData"/> with the database.
    /// </summary>
    public const string TranslationExportPlan = "translation.export.plan";

    /// <summary>
    /// Exports the whole configured translation database as one tab-separated text or XML file into the Host's new, empty
    /// folder. Arguments: <see cref="ExportArgs"/> with the folder and the file name, its extension included; data:
    /// <see cref="ExportRunData"/> with the database.
    /// </summary>
    public const string TranslationExportRun = "translation.export.run";

    /// <summary>
    /// The plan of eplan_page_delete: finds the pages by their exact names and the devices their removal leaves without a
    /// main function, never writes. Arguments: <see cref="PageDeleteArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string PageDeletePlan = "page.delete.plan";

    /// <summary>
    /// Backs the project up, then removes the planned pages in one transaction and reads back (ADR-0013). Arguments:
    /// <see cref="PageDeleteArgs"/> with the fingerprint and the backup target; data: <see cref="PageDeleteData"/>.
    /// </summary>
    public const string PageDeleteExecute = "page.delete.execute";

    /// <summary>
    /// The plan of eplan_device_renumber: finds the pages and the devices in scope, never writes; which tags EPLAN gives is
    /// known only after running. Arguments: <see cref="DeviceRenumberArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string DeviceRenumberPlan = "device.renumber.plan";

    /// <summary>
    /// Backs the project up, then lets EPLAN number the device tags in scope in one transaction and reads back (ADR-0013).
    /// Arguments: <see cref="DeviceRenumberArgs"/> with the fingerprint and the backup target; data: <see cref="DeviceRenumberData"/>.
    /// </summary>
    public const string DeviceRenumberExecute = "device.renumber.execute";

    /// <summary>
    /// The plan of eplan_page_renumber: finds the pages by their exact names, never writes; the new names are known only
    /// after running. Arguments: <see cref="PageRenumberArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string PageRenumberPlan = "page.renumber.plan";

    /// <summary>
    /// Backs the project up, then lets EPLAN renumber the pages per structure in one transaction and reads back
    /// (ADR-0013). Arguments: <see cref="PageRenumberArgs"/> with the fingerprint and the backup target; data:
    /// <see cref="PageRenumberData"/>.
    /// </summary>
    public const string PageRenumberExecute = "page.renumber.execute";

    /// <summary>
    /// The plan of eplan_language_set: reads the project's language settings and works out the new ones, never writes.
    /// Arguments: <see cref="LanguageSetArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string LanguageSetPlan = "language.set.plan";

    /// <summary>
    /// Adds translation languages and sets the displayed languages, then reads them back; on any failure writes the old
    /// settings back, which a rollback does not (api-notes A28). Arguments: <see cref="LanguageSetArgs"/> with the
    /// fingerprint; data: <see cref="LanguageSetData"/>.
    /// </summary>
    public const string LanguageSetExecute = "language.set.execute";

    /// <summary>
    /// The plan of eplan_language_remove: checks the languages against the project's settings and counts the texts with
    /// translations in them, never writes. Arguments: <see cref="LanguageRemoveArgs"/>; data: <see cref="WritePlan"/>
    /// with details.
    /// </summary>
    public const string LanguageRemovePlan = "language.remove.plan";

    /// <summary>
    /// Backs the project up, then removes the languages with their translations in one transaction and reads the settings
    /// back; on any failure writes the old settings back after the rollback (ADR-0013, api-notes A28). Arguments:
    /// <see cref="LanguageRemoveArgs"/> with the fingerprint and the backup target; data: <see cref="LanguageRemoveData"/>.
    /// </summary>
    public const string LanguageRemoveExecute = "language.remove.execute";

    /// <summary>
    /// The plan of eplan_project_translate: checks the source and translation languages and counts the texts in the
    /// scope that miss a translation language, never writes; what EPLAN fills in is known only after running. Arguments:
    /// <see cref="ProjectTranslateArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string ProjectTranslatePlan = "project.translate.plan";

    /// <summary>
    /// Backs the project up, then lets EPLAN translate the scope with the translation database in one transaction and
    /// reads the texts back (ADR-0013). Arguments: <see cref="ProjectTranslateArgs"/> with the fingerprint and the backup
    /// target; data: <see cref="ProjectTranslateData"/>.
    /// </summary>
    public const string ProjectTranslateExecute = "project.translate.execute";

    /// <summary>
    /// The plan of eplan_terminal_renumber: checks the scheme and finds the strips and the terminals in scope, never
    /// writes; the new names are known only after running. Arguments: <see cref="TerminalRenumberArgs"/>; data:
    /// <see cref="WritePlan"/> with details.
    /// </summary>
    public const string TerminalRenumberPlan = "terminal.renumber.plan";

    /// <summary>
    /// Backs the project up, then lets EPLAN number the terminals in scope in one transaction and reads back (ADR-0013).
    /// Arguments: <see cref="TerminalRenumberArgs"/> with the fingerprint and the backup target; data: <see cref="TerminalRenumberData"/>.
    /// </summary>
    public const string TerminalRenumberExecute = "terminal.renumber.execute";

    /// <summary>
    /// The plan of eplan_cable_renumber: checks the scheme and finds the cables in scope, never writes; the new tags are
    /// known only after running. Arguments: <see cref="CableRenumberArgs"/>; data: <see cref="WritePlan"/> with details.
    /// </summary>
    public const string CableRenumberPlan = "cable.renumber.plan";

    /// <summary>
    /// Backs the project up, then lets EPLAN number the cables in scope in one transaction and reads back (ADR-0013).
    /// Arguments: <see cref="CableRenumberArgs"/> with the fingerprint and the backup target; data: <see cref="CableRenumberData"/>.
    /// </summary>
    public const string CableRenumberExecute = "cable.renumber.execute";

    /// <summary>
    /// The plan of eplan_connection_number: checks the scheme and counts the project's connections and wire numbers, never
    /// writes; the new wire numbers are known only after running. Arguments: <see cref="ConnectionNumberArgs"/>; data:
    /// <see cref="WritePlan"/> with details.
    /// </summary>
    public const string ConnectionNumberPlan = "connection.number.plan";

    /// <summary>
    /// Backs the project up, then lets EPLAN number the project's connections in one transaction and reads back
    /// (ADR-0013). Arguments: <see cref="ConnectionNumberArgs"/> with the fingerprint and the backup target; data:
    /// <see cref="ConnectionNumberData"/>.
    /// </summary>
    public const string ConnectionNumberExecute = "connection.number.execute";

    /// <summary>
    /// Backs the configured master-data database up with EPLAN's <c>backup /TYPE:MASTERDATA</c> into the Host's new,
    /// empty folder, before a master-data write runs (ADR-0019); changes no database. Arguments:
    /// <see cref="MasterDataBackupArgs"/>; data: <see cref="MasterDataBackupData"/>.
    /// </summary>
    public const string MasterDataBackup = "masterdata.backup";

    /// <summary>
    /// The plan of eplan_part_import: reads the parts XML file the Host resolved below its import root and classifies its
    /// part numbers against the configured parts database, never writes. Arguments: <see cref="PartImportArgs"/>; data:
    /// <see cref="WritePlan"/> with the master data and details, and no project.
    /// </summary>
    public const string PartImportPlan = "part.import.plan";

    /// <summary>
    /// What the Bridge recorded for a write's request ID (ADR-0010). Answered on the pipe thread, never dispatched to
    /// EPLAN's main thread, so it works while a write is running. Arguments: <see cref="OutcomeGetArgs"/>; data:
    /// <see cref="OutcomeGetData"/>.
    /// </summary>
    public const string OutcomeGet = "outcome.get";

    /// <summary>
    /// Asks the Bridge to stop a queued or running invoke, by its request ID (M4 design 4.3). Sent on another connection,
    /// because the invoke's own connection reads nothing until it answers, and answered on the pipe thread like
    /// <see cref="OutcomeGet"/>. The invoke stops at its next safe point and never in the middle of an EPLAN call; a
    /// write that has committed stays committed, and outcome.get tells what became of it. Arguments:
    /// <see cref="OperationCancelArgs"/>; data: <see cref="OperationCancelData"/>.
    /// </summary>
    public const string OperationCancel = "operation.cancel";

    /// <summary>
    /// Asks the engineer to approve a write in a dialog in EPLAN, modal on the main thread until answered or the
    /// deadline (M3 design 4.3, 4.7); other calls get EPLAN_BUSY meanwhile. Arguments: <see cref="ApprovalAskArgs"/>;
    /// data: <see cref="ApprovalAskData"/>.
    /// </summary>
    public const string ApprovalAsk = "approval.ask";
}
