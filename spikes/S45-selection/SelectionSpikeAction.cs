using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;
using EplanMcp.Spikes.S42;
using Newtonsoft.Json;

namespace EplanMcp.Spikes.S45;

/// <summary>Deferred main-thread Large probe; all fixtures are new LargeWrite pages supplied by SelectionSpikeTests.</summary>
public sealed class SelectionSpikeAction : IEplAction
{
    private static int _running;
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpSelectionSpike"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }
    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        { return false; }
        try
        {
            string Read(string name)
            { string value = string.Empty; oActionCallingContext.GetParameter(name, ref value); return value; }
            string log = Evidence.LogPath(Read("Log"), false), pageA = Read("PageA"), pageB = Read("PageB");
            if (string.IsNullOrWhiteSpace(pageA) || string.IsNullOrWhiteSpace(pageB) || pageA == pageB)
            { throw new ArgumentException("Supply two distinct new fixture pages."); }
            using (new FileStream(log, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { }
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            new Thread(() => Later(dispatcher, log, pageA, pageB)) { IsBackground = true }.Start();
            return true;
        }
        catch (Exception ex) { Trace.TraceError("S45 rejected: {0}", ex); Interlocked.Exchange(ref _running, 0); return false; }
    }
    private static void Later(Dispatcher dispatcher, string log, string pageA, string pageB)
    {
        void Print(string phase, object data) => File.AppendAllText(log, JsonConvert.SerializeObject(new { phase, data }) + "\n", new UTF8Encoding(false));
        try
        {
            Thread.Sleep(300); // A9/S42: the remote action must return before the independent dispatcher call.
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(60), new Func<bool>(() =>
            {
                try
                {
                    using var locking = new LockingStep();
                    using var process = Process.GetCurrentProcess();
                    if (!IsWindowEnabled(process.MainWindowHandle))
                    { throw new InvalidOperationException("Close modal dialogs first."); }
                    using var manager = new ProjectManager();
                    bool previousLock = manager.LockProjectByDefault;
                    Project[] opened;
                    try
                    { manager.LockProjectByDefault = false; opened = manager.OpenProjects; }
                    finally { manager.LockProjectByDefault = previousLock; }
                    Project project = opened.Single();
                    Evidence.ProjectPath(project.ProjectLinkFilePath);
                    var selection = new SelectionSet { LockProjectByDefault = false, LockSelectionByDefault = false };
                    if (selection.GetCurrentProject(false)?.ProjectFullName != project.ProjectFullName)
                    { throw new InvalidOperationException("Only current LargeWrite is allowed."); }
                    Page a = project.Pages.Single(p => p.Name == pageA), b = project.Pages.Single(p => p.Name == pageB);
                    Function function = a.AllPlacements.OfType<Function>().Single();
                    Placement graphic = b.AllPlacements.Single(p => p.GetType() == typeof(Line));
                    var originalEditors = selection.OpenedPages.Select(p => p.Name).ToArray();
                    var originalSelection = selection.Selection.Select(o => o.ToStringIdentifier()).ToArray();
                    Print("environment", new { version = FileVersionInfo.GetVersionInfo(typeof(Project).Assembly.Location).FileVersion, originalEditors, originalSelection });
                    using var edit = new Edit();
                    using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                    object[] Rows() => selection.Selection.Select(o => (object)new
                    {
                        type = o.GetType().Name,
                        id = o.ToStringIdentifier(),
                        owner = o.Project?.ProjectFullName,
                        page = o is Placement placement ? placement.Page?.Name : null,
                        displayed = o is PropertyPlacement property ? property.GetDisplayString() : null
                    }).ToArray();
                    void Select(string phase, params StorableObject[] objects)
                    {
                        var ids = new StringCollection();
                        foreach (StorableObject o in objects)
                        { string id = o.ToStringIdentifier(); ids.Add(id.Substring(id.IndexOf('/') + 1)); }
                        var before = Evidence.Hashes(project.ProjectLinkFilePath);
                        try
                        { edit.SelectObjects(project.ProjectLinkFilePath, ids, true); }
                        catch (Exception ex) { Print(phase + "_exception", Evidence.Error(ex)); }
                        Print(phase, new
                        {
                            requested = objects.Select(o => new { type = o.GetType().Name, id = o.ToStringIdentifier() }).ToArray(),
                            actual = Rows(),
                            currentProject = selection.GetCurrentProject(false)?.ProjectFullName,
                            currentPage = (selection.CurrentlyEdited as Page)?.Name,
                            openedPages = selection.OpenedPages.Select(p => p.Name).ToArray(),
                            designBefore = before["design"],
                            designAfter = Evidence.Hashes(project.ProjectLinkFilePath)["design"]
                        });
                    }
                    try
                    {
                        foreach (Page open in selection.OpenedPages)
                        { if (!edit.ClosePage(open)) { throw new InvalidOperationException("Editor did not close."); } }
                        if (selection.OpenedPages.Length != 0)
                        { throw new InvalidOperationException("No-editor precondition failed."); }
                        Print("no_editor_before", new { current = selection.CurrentlyEdited?.GetType().Name, selected = Rows() });
                        Select("no_editor", function);
                        edit.OpenPageWithName(project.ProjectLinkFilePath, a.Name);
                        Select("one_page", function);
                        Select("different_pages", function, graphic);
                        edit.OpenPageWithName(project.ProjectLinkFilePath, a.Name);
                        PropertyPlacement property = function.PropertyPlacements.First();
                        using AnyPropertyId displayed = property.DisplayedProperty;
                        Print("property_identity", new
                        {
                            type = property.GetType().Name,
                            parent = function.ToStringIdentifier(),
                            property = property.ToStringIdentifier(),
                            property.ObjectIdentifier,
                            displayed = displayed.AsInt,
                            property.Location.X,
                            property.Location.Y
                        });
                        Select("property_placement", property);
                        edit.ClearSelection();
                        Print("clear", new { actual = Rows(), currentPage = (selection.CurrentlyEdited as Page)?.Name });
                    }
                    finally
                    {
                        edit.ClearSelection();
                        foreach (Page open in selection.OpenedPages)
                        { edit.ClosePage(open); }
                        foreach (string page in originalEditors.Reverse())
                        { edit.OpenPageWithName(project.ProjectLinkFilePath, page); }
                        Print("restored_editors", selection.OpenedPages.Select(p => p.Name).ToArray());
                    }
                    Print("completed", true);
                    return true;
                }
                catch (Exception ex) { Print("failure", Evidence.Error(ex)); return false; }
            }));
            if (done is not true)
            { Print("failure", "Dispatcher did not complete successfully."); }
        }
        catch (Exception ex) { Trace.TraceError("S45 failed: {0}", ex); }
        finally { Interlocked.Exchange(ref _running, 0); }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
}
