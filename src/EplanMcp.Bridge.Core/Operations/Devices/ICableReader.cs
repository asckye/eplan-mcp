namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read access to cables, implemented by the EPLAN adapter layer; main thread only, inside the dispatcher's LockingStep.</summary>
public interface ICableReader
{
    /// <summary>
    /// The device whose main function's full tag is exactly <paramref name="tag"/>, found as
    /// <see cref="IDeviceReader.ReadDevice"/> finds it, with its cable data when it is a cable; null when there is none.
    /// Texts are read in <paramref name="language"/> (null: EPLAN's user interface language); an unknown one throws INVALID_ARGUMENT.
    /// </summary>
    public CableRecord? ReadCable(string projectPath, string tag, string? language);
}
