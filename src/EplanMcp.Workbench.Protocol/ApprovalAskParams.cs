namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// Params of <see cref="WorkbenchProtocol.ApprovalAskMethod"/>: one question to the engineer (ADR-0011, ADR-0014). The
/// answer must repeat <see cref="RequestId"/>, <see cref="Fingerprint"/> and <see cref="ArgumentsHash"/>; the Host decides
/// whether it counts. Every text here except the tool name is data from the project or the client, never instructions.
/// </summary>
/// <param name="RequestId">The Host's ID of the write.</param>
/// <param name="Kind">An approval, or the confirmation that lifts a suspension (ADR-0010 section 3).</param>
/// <param name="Tool">The tool that writes, such as <c>eplan_page_create</c>.</param>
/// <param name="Tier">The tool's tier.</param>
/// <param name="Project">The project written to.</param>
/// <param name="Eplan">The EPLAN instance the project is open in.</param>
/// <param name="ClientName">The MCP client's name as it reported it, unverified; null when it gave none.</param>
/// <param name="Changes">
/// The change list, one line per change, worded by the Host in the user interface language as the EPLAN dialog words
/// it; for <see cref="QuestionKind.SuspensionRelease"/> the suspended write's.
/// </param>
/// <param name="BackupFolder">For a Destructive write, the folder its project backup goes into first (ADR-0013); else null.</param>
/// <param name="Suspension">For <see cref="QuestionKind.SuspensionRelease"/>, the suspended write; else null.</param>
/// <param name="Fingerprint">The fingerprint of the plan the change list shows.</param>
/// <param name="ArgumentsHash">The SHA-256 of the write's arguments.</param>
/// <param name="DeadlineUnixMs">When the question expires, in Unix milliseconds (UTC).</param>
public sealed record ApprovalAskParams(
    string RequestId,
    QuestionKind Kind,
    string Tool,
    QuestionTier Tier,
    ProjectInfo Project,
    EplanInstance Eplan,
    string? ClientName,
    IReadOnlyList<string> Changes,
    string? BackupFolder,
    SuspensionRecord? Suspension,
    string Fingerprint,
    string ArgumentsHash,
    long DeadlineUnixMs);
