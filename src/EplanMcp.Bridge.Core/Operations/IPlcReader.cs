namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read access to PLC boxes, implemented by the EPLAN adapter layer; main thread only, inside the dispatcher's LockingStep.</summary>
public interface IPlcReader
{
    /// <summary>
    /// The device whose main function's full tag is exactly <paramref name="tag"/>, found as
    /// <see cref="IDeviceReader.ReadDevice"/> finds it, with its PLC data when it is a PLC box; null when there is none.
    /// Texts are read in <paramref name="language"/> (null: EPLAN's user interface language); an unknown one throws INVALID_ARGUMENT.
    /// Native owner, manual address, card reference and parentless representations are read only when <paramref name="detailed"/> is true.
    /// </summary>
    public PlcRecord? ReadPlc(string projectPath, string tag, string? language, bool detailed = false);
}
