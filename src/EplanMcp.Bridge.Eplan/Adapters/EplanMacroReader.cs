using Eplan.EplApi.Base;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Resolves the macro directory through EPLAN's path variables. Main thread, inside the dispatcher's LockingStep.</summary>
internal sealed class EplanMacroReader : IMacroReader
{
    public string? MacroRoot()
    {
        MainThread.Assert();

        // A variable EPLAN cannot resolve comes back unchanged (api-notes A4: $(MD_MACROS) resolves in 2026).
        string resolved = PathMap.SubstitutePath("$(MD_MACROS)");
        return string.IsNullOrWhiteSpace(resolved) || resolved.Contains("$(") ? null : resolved;
    }
}
