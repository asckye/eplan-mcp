namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads pages' connections and generates connections on them, implemented by the EPLAN adapter layer
/// (docs/tools/eplan_connection_generate.md, section 5). Page names are exact, as the project has them; every list
/// holds one entry per page, in the order of <c>pages</c>. Main thread only; <see cref="Generate"/> only inside the
/// write's transaction.
/// </summary>
public interface IConnectionGenerator
{
    /// <summary>The 2D connections placed on each page, read as connection.list reads a page (api-notes A11).</summary>
    public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages);

    /// <summary>The session identifier (<c>ToStringIdentifier</c>, valid in this EPLAN session only, A3) of every object placed on each page.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages);

    /// <summary>
    /// Turns the connections drawn on the pages into connections, updating only and never rebuilding all (A13, A14;
    /// tool spec 2). EPLAN's failure is EPLAN_OPERATION_FAILED.
    /// </summary>
    public void Generate(string projectPath, IReadOnlyList<string> pages);
}
