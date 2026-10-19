namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Read access to the connections of a project, implemented by the EPLAN adapter layer. Called on the main thread only,
/// inside the dispatcher's LockingStep, with a project path and device tag resolved in the same call. It returns plain
/// records; finding the device, checking arguments and paging stay in Bridge.Core.
/// </summary>
public interface IConnectionReader
{
    /// <summary>
    /// The connections of the open project at <paramref name="projectPath"/> in EPLAN's order: with
    /// <paramref name="device"/>, those at the main or an auxiliary function of the device whose main function has
    /// exactly this full tag (none when there is no such device); with <paramref name="page"/>, those placed on the page
    /// whose full name is exactly this; with both, those meeting both. At least one is given. Null when the project
    /// has no page of that name.
    /// </summary>
    public IReadOnlyList<ConnectionRecord>? ReadConnections(string projectPath, string? device, string? page);
}
