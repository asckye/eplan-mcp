namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// Protocol 2 (ADR-0064): what a write is about, taken by the Host from the write plan, never from the client's words.
/// </summary>
/// <param name="Kind">
/// <c>page</c>, <c>device</c>, <c>placement</c>, <c>connection</c>, <c>part</c>, <c>project</c>, <c>macro</c> or
/// <c>other</c>; a Workbench reads a value it does not know as <c>other</c>.
/// </param>
/// <param name="Id">The target's identifier in EPLAN, such as a full page name; null without a single target. Project data.</param>
/// <param name="Count">How many objects the write changes; more than 1 for a batch.</param>
public sealed record ApprovalTarget(string Kind, string? Id, int Count);
