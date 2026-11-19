using System.Runtime.CompilerServices;
using Eplan.EplApi.Base;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Path variables resolved for a project where the platform can. <c>PathMap.SubstitutePath(String, String)</c> exists
/// from EPLAN 2026; the 2025 API documents only <c>SubstitutePath(String)</c> (eplan.help API pages for 2025 and 2026,
/// read 2026-11-19; reported on 2025.0.3.23616 as a MissingMethodException that failed every page read).
/// </summary>
internal static class EplanPaths
{
    private static bool _projectOverloadMissing;

    /// <summary>
    /// <paramref name="path"/> with EPLAN's path variables replaced. Without the project overload the project's own
    /// variables stay unresolved, as the platform leaves them.
    /// </summary>
    internal static string Substitute(string path, string projectPath)
    {
        if (!_projectOverloadMissing)
        {
            try
            {
                return ForProject(path, projectPath);
            }
            catch (MissingMethodException)
            {
                // Thrown when the runtime compiles ForProject, before any of it runs; later calls skip the attempt.
                _projectOverloadMissing = true;
            }
        }

        return PathMap.SubstitutePath(path);
    }

    /// <summary>Kept out of line so that a platform without the overload fails here and not in the caller.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ForProject(string path, string projectPath) => PathMap.SubstitutePath(path, projectPath);
}
