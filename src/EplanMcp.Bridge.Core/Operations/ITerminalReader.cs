using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read access to terminal strips, implemented by the EPLAN adapter layer; main thread only, inside the dispatcher's LockingStep.</summary>
public interface ITerminalReader
{
    /// <summary>Every terminal strip definition of the open project at <paramref name="projectPath"/>, in EPLAN's order.</summary>
    public IReadOnlyList<TerminalStripListItem> ReadStrips(string projectPath);
}
