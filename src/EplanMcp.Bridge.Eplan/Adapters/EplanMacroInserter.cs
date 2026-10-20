using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanInsert = Eplan.EplApi.HEServices.Insert;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads the page a window macro goes on, and inserts the macro the way spike S10 did in the dispatcher (api-notes A14):
/// <c>WindowMacro.Open</c>, then <c>Insert.WindowMacro(macro, MultiLine, variant, page, point, MoveKind.Absolute,
/// numeration)</c>. The inserted functions are those on the page that were not there before, read with
/// <c>FunctionsFilter.Page</c> as S10 read them inside the SafetyPoint. Main thread; <see cref="Insert"/> inside the
/// write's TransactionScope, whose SafetyPoint is the only wrapper that takes an insert back (A13).
/// </summary>
internal sealed class EplanMacroInserter : IMacroInserter
{
    /// <summary>EPLAN's message number for "the same symbols placed on top of each other" (spike S9, A13).</summary>
    private const string StackedSymbols = "S029181";

    public MacroTargetPage? ReadPage(string projectPath, string pageName)
    {
        MainThread.Assert();
        Page? page = Find(EplanProjects.ByPath(projectPath), pageName);
        if (page is null)
        {
            return null;
        }

        PointD? size = Size(page);
        return new MacroTargetPage
        {
            Name = page.Name,
            Type = page.PageType.ToString(),
            Width = size?.X,
            Height = size?.Y,

            // Every placement on the page, groups resolved (EPLAN XML docs, Page.AllPlacements).
            Objects = [.. page.AllPlacements.Select(placement => placement.ToStringIdentifier())],
        };
    }

    public PlacedMacro Insert(string projectPath, string pageName, string macroPath, int variant, double x, double y, bool number)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        Page page = Find(project, pageName) ?? throw new InvalidOperationException("The planned page is gone.");
        var before = new HashSet<string>(Functions(project, page).Select(f => f.ToStringIdentifier()), StringComparer.Ordinal);
        using var macro = new WindowMacro();
        macro.Open(macroPath, project);
        using var insert = new EplanInsert();
        StorableObject[] placed;
        try
        {
            placed = insert.WindowMacro(
                macro,
                WindowMacro.Enums.RepresentationType.MultiLine,
                variant,
                page,
                new PointD(x, y),
                EplanInsert.MoveKind.Absolute,
                number ? WindowMacro.Enums.NumerationMode.Number : WindowMacro.Enums.NumerationMode.None);
        }
        catch (BaseException ex) when (ex.Message.IndexOf(StackedSymbols, StringComparison.Ordinal) >= 0)
        {
            var stacked = new BridgeErrorException(
                ErrorReason.InvalidArgument,
                "EPLAN refuses to place the macro there because the same symbols are at that position already, so nothing was inserted. Choose another position; eplan_page_get shows the devices on the page.",
                ex);
            stacked.Error.Metadata = new Dictionary<string, string> { ["page"] = pageName };
            throw stacked;
        }

        return new PlacedMacro
        {
            Functions =
            [
                .. Functions(project, page).Where(f => !before.Contains(f.ToStringIdentifier())).Select(f => new MacroFunctionRecord
                {
                    Name = f.Name ?? string.Empty,
                    VisibleName = f.VisibleName ?? string.Empty,
                    Category = f.FunctionCategory.ToString(),
                    Main = f.IsMainFunction,
                }),
            ],
            ObjectCount = placed.Length,
        };
    }

    /// <summary>The page whose full name is exactly <paramref name="pageName"/>: EPLAN's exact name filter (S6), checked ordinally.</summary>
    private static Page? Find(Project project, string pageName)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        return finder.GetPages(filter).FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
    }

    /// <summary>The functions on the page: <c>FunctionsFilter.Page</c>, not the shared <c>Page.Filter</c> (BannedSymbols.txt).</summary>
    private static Function[] Functions(Project project, Page page)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new FunctionsFilter { Page = page };
        return finder.GetFunctions(filter);
    }

    /// <summary>
    /// The page's size, or null when EPLAN cannot tell it: S10 got InvalidOperationException ("S063136") on a new, empty
    /// page (A14), and the XML docs name BaseException for a page without a frame.
    /// </summary>
    private static PointD? Size(Page page)
    {
        try
        {
            return page.Size;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (BaseException)
        {
            return null;
        }
    }
}
