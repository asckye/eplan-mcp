namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and renames devices, implemented by the EPLAN adapter layer (docs/tools/eplan_device_update.md, section 5).
/// Main thread only; <see cref="Rename"/> only inside the write's transaction.
/// </summary>
public interface IDeviceRenamer
{
    /// <summary>The device whose main function's full tag is exactly <paramref name="tag"/> (ordinal); null when there is none.</summary>
    public DeviceNames? ReadDevice(string projectPath, string tag);

    /// <summary>The name of every function of the project, main and auxiliary, for checking that a new tag is free.</summary>
    public IReadOnlyList<string> FunctionNames(string projectPath);

    /// <summary>
    /// Gives every function named exactly <paramref name="tag"/> the name <paramref name="newTag"/>, the main function
    /// first (spike S9: renaming only the main one detaches the auxiliary functions, A13), and reads back: the main
    /// function's name and shown name, and how many functions carry the old and the new tag.
    /// </summary>
    public RenameReadBack Rename(string projectPath, string tag, string newTag);
}
