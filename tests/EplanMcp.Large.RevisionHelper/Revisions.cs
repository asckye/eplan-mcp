using System.Runtime.CompilerServices;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.System;

namespace EplanMcp.Large.RevisionHelper;

/// <summary>The first EPLAN types, only touched once the resolver points at EPLAN (as in the offline host, S15).</summary>
internal static class Revisions
{
    /// <summary>
    /// Init without licence or login dialogs (A22), the copy opened in a LockingStep as the offline host opens its project,
    /// <c>Revision.CreateRevision(project, name, description, ref path)</c> as S23 r3 called it (outside a SafetyPoint: it
    /// cannot be undone anyway), and the copy's link file read before closing. Exit once, on this thread (B5).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Create(string variantBin, string projectFile, string name)
    {
        var application = new EplApplication { EplanBinFolder = variantBin };
        application.Init(string.Empty, false, false);
        try
        {
            using (new LockingStep())
            {
                using var manager = new ProjectManager();
                Project project = manager.OpenProject(projectFile);
                try
                {
                    string newPath = string.Empty;
                    using (var revision = new Revision())
                    {
                        revision.CreateRevision(project, name, "EplanMcp Large test", ref newPath);
                    }

                    return project.ProjectLinkFilePath;
                }
                finally
                {
                    project.Close();
                }
            }
        }
        finally
        {
            application.Exit();
        }
    }
}
