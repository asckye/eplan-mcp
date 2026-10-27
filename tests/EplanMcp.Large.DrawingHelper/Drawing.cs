using System.Runtime.CompilerServices;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.System;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json;

namespace EplanMcp.Large.DrawingHelper;

internal static class Drawing
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Read(string variantBin, string projectFile, string pageName)
    {
        var application = new EplApplication { EplanBinFolder = variantBin };
        application.Init(string.Empty, false, false);
        try
        {
            using (new LockingStep())
            {
                MainThread.Use(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                using var manager = new ProjectManager();
                Project project = manager.OpenProject(projectFile);
                try
                {
                    return JsonConvert.SerializeObject(new EplanDrawingReader().Read(project.ProjectFullName, pageName)
                        ?? throw new InvalidOperationException("The fixture page was not found."));
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
