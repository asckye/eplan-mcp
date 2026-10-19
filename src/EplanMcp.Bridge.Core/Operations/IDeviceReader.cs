namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Read access to the functions (devices) of a project, implemented by the EPLAN adapter layer. Called on the main
/// thread only, inside the dispatcher's LockingStep. It returns plain records; matching tags, filtering and paging
/// stay in Bridge.Core, where they can be tested without EPLAN.
/// </summary>
public interface IDeviceReader
{
    /// <summary>Whether <paramref name="category"/> names an EPLAN function category, ignoring case. Reads no project.</summary>
    public bool IsCategory(string category);

    /// <summary>
    /// The functions of the open project at <paramref name="projectPath"/>, main and auxiliary, in EPLAN's order;
    /// when given, only those placed on the page whose full name is exactly <paramref name="page"/> and of
    /// <paramref name="category"/>. Null when the project has no page of that name.
    /// </summary>
    public IReadOnlyList<FunctionRecord>? ReadFunctions(string projectPath, string? page, string? category);
}
