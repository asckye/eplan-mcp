namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and numbers device tags for eplan_device_renumber (docs/tools/eplan_device_renumber.md, section 5), implemented
/// by the EPLAN adapter layer. Main thread only; <see cref="Renumber"/> only inside the write's transaction.
/// </summary>
public interface IDeviceRenumberer
{
    /// <summary>
    /// The functions on the page whose full name is exactly <paramref name="page"/>, or of the whole project when it is
    /// null, with their session identifiers; null when there is no such page.
    /// </summary>
    public IReadOnlyList<PlacedFunction>? ReadFunctions(string projectPath, string? page);

    /// <summary>
    /// Lets EPLAN number the functions on <paramref name="pages"/> (null: the whole project) with
    /// <c>Renumber.DeviceTags</c>: only tags with <c>?</c> when <paramref name="onlyUnnumbered"/>, in
    /// <paramref name="scheme"/> (null: EPLAN's default), from <paramref name="start"/> in steps of <paramref name="increment"/>.
    /// A scheme EPLAN does not know throws INVALID_ARGUMENT with the schemes it names.
    /// </summary>
    public void Renumber(string projectPath, IReadOnlyList<string>? pages, bool onlyUnnumbered, string? scheme, int start, int increment);
}
