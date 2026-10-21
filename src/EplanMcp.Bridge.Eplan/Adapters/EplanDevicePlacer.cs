using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Places a part's device the way spike S20 did in the dispatcher (api-notes A26):
/// <c>new DeviceService().CreateDevice(part, variant, page, point)</c>, which inserts the part's macro and returns all the
/// device's functions, the main one placed and the rest unplaced, tagged <c>=+-Q1</c>. Each returned function is then
/// given the tag by assigning <c>Function.Name</c>, the main function first, as spike S9 renamed a device (A13): the
/// returned objects, never a name lookup, since other functions may be called <c>=+-Q1</c> too. New functions on the
/// page are counted with <c>FunctionsFilter.Page</c>, as EplanMacroInserter counts them. Main thread; <see cref="Place"/>
/// inside the write's TransactionScope, whose SafetyPoint takes the placement back (S20).
/// </summary>
internal sealed class EplanDevicePlacer : IDevicePlacer
{
    public MacroTargetPage? ReadPage(string projectPath, string pageName) => new EplanMacroInserter().ReadPage(projectPath, pageName);

    public PlacedDevice Place(string projectPath, string pageName, string part, string variant, double x, double y, string tag)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        Page page = Find(project, pageName) ?? throw new InvalidOperationException("The planned page is gone.");
        var before = new HashSet<string>(OnPage(project, page), StringComparer.Ordinal);
        Function[] created;
        try
        {
            using var devices = new DeviceService();
            created = devices.CreateDevice(part, variant, page, new PointD(x, y)) ?? [];
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or ApplicationException && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                $"EPLAN could not place part '{part}', so nothing was changed. metadata.eplanMessage has EPLAN's message; check the part's macro in EPLAN's parts management, or insert a window macro with eplan_macro_insert.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["part"] = part, ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }

        Function[] ordered = [.. created.Where(f => f.IsMainFunction), .. created.Where(f => !f.IsMainFunction)];
        foreach (Function function in ordered)
        {
            function.Name = tag;
        }

        Function? main = ordered.FirstOrDefault(f => f.IsMainFunction);
        return new PlacedDevice
        {
            Names = [.. ordered.Select(f => f.Name ?? string.Empty)],
            MainName = main?.Name ?? string.Empty,
            VisibleName = main?.VisibleName ?? string.Empty,
            Category = main?.FunctionCategory.ToString() ?? string.Empty,
            Unplaced = created.Count(f => !f.IsPlaced),
            OnPage = OnPage(project, page).Count(id => !before.Contains(id)),
        };
    }

    /// <summary>The page whose full name is exactly <paramref name="pageName"/>: EPLAN's exact name filter (S6), checked ordinally.</summary>
    private static Page? Find(Project project, string pageName)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        return finder.GetPages(filter).FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
    }

    /// <summary>The session identifiers of the functions on the page: <c>FunctionsFilter.Page</c>, not the shared <c>Page.Filter</c>.</summary>
    private static string[] OnPage(Project project, Page page)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new FunctionsFilter { Page = page };
        return [.. finder.GetFunctions(filter).Select(f => f.ToStringIdentifier())];
    }
}
