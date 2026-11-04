namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and renames devices, implemented by the EPLAN adapter layer (docs/tools/eplan_device_update.md, section 5).
/// Main thread only; <see cref="Rename"/> only inside the write's transaction.
/// </summary>
public interface IDeviceRenamer
{
    /// <summary>The device or terminal-only PLC module whose full tag is exactly <paramref name="tag"/> (ordinal); null when there is none.</summary>
    public DeviceNames? ReadDevice(string projectPath, string tag);

    /// <summary>The name of every function of the project, main and auxiliary, for checking that a new tag is free.</summary>
    public IReadOnlyList<string> FunctionNames(string projectPath);

    /// <summary>
    /// Renames the whole device with NameService (A85), preserving suffixes. The returned names are provisional;
    /// the caller reacquires the device after commit because native wrappers and unplaced IDs can change.
    /// </summary>
    public RenameReadBack Rename(string projectPath, string tag, string newTag);
}
