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

    /// <summary>The names of every main function of the project, in EPLAN's order; cheaper than <see cref="ReadFunctions"/>.</summary>
    public IReadOnlyList<FunctionName> ReadMainFunctionNames(string projectPath);

    /// <summary>
    /// The device whose main function's full tag is exactly <paramref name="tag"/> (ordinal), the first one in EPLAN's
    /// order if a project repeats a tag; null when there is none. Texts are read in <paramref name="language"/> (null:
    /// EPLAN's user interface language), which is checked first: an unknown one throws INVALID_ARGUMENT.
    /// </summary>
    public DeviceRecord? ReadDevice(string projectPath, string tag, string? language, bool withProperties);
}
