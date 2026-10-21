using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read access to terminal strips, implemented by the EPLAN adapter layer; main thread only, inside the dispatcher's LockingStep.</summary>
public interface ITerminalReader
{
    /// <summary>Every terminal strip definition of the open project at <paramref name="projectPath"/>, in EPLAN's order.</summary>
    public IReadOnlyList<TerminalStripListItem> ReadStrips(string projectPath);

    /// <summary>
    /// The strip whose name is exactly <paramref name="name"/> (ordinal) with all its terminals in the strip's order; null
    /// when there is none. Designations are left to Bridge.Core, ends hold the names EPLAN gives, empty ones included.
    /// </summary>
    public TerminalStripRecord? ReadStrip(string projectPath, string name);
}
