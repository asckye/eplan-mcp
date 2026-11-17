namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads connections and numbers them for eplan_connection_number (docs/tools/eplan_connection_number.md, section 5),
/// implemented by the EPLAN adapter layer. Main thread only; <see cref="Designate"/> only inside the write's transaction.
/// </summary>
public interface IConnectionNumberer
{
    /// <summary>The names of the schemes under the project setting <c>WireNumberingLog.Schema</c>, in EPLAN's order (A24).</summary>
    public IReadOnlyList<string> Schemes(string projectPath);

    /// <summary>Every connection of the project, 3D ones included, with its wire number, in EPLAN's order.</summary>
    public IReadOnlyList<DesignatedConnection> ReadConnections(string projectPath);

    /// <summary>
    /// Lets EPLAN number the connections of the whole project with <c>ConnectionService.DesignateConnections</c> in
    /// <paramref name="scheme"/>, replacing existing wire numbers as <paramref name="overwrite"/> (one of
    /// <c>ConnectionNumberArgs.OverwriteModes</c>) says. A scheme EPLAN refuses throws INVALID_ARGUMENT, other EPLAN
    /// failures EPLAN_OPERATION_FAILED.
    /// </summary>
    public void Designate(string projectPath, string scheme, string overwrite);
}
