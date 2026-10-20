namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Where EPLAN keeps its window macros, implemented by the EPLAN adapter layer. Called on the main thread only, inside
/// the dispatcher's LockingStep. Scanning and every path check stay in Bridge.Core (<see cref="MacroFiles"/>), where
/// they can be tested without EPLAN.
/// </summary>
public interface IMacroReader
{
    /// <summary>
    /// The macro directory, <c>$(MD_MACROS)</c>, as EPLAN resolves it (api-notes A4); null when EPLAN cannot resolve it.
    /// </summary>
    public string? MacroRoot();
}
