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

    /// <summary>
    /// Lists the parts a project uses, summed up per part number and variant, filtered and sliced in the Bridge.
    /// Arguments: <see cref="PartListArgs"/>; data: <see cref="ListData{TItem}"/> of <see cref="PartListItem"/>.
    /// </summary>
    public const string PartList = "part.list";

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
