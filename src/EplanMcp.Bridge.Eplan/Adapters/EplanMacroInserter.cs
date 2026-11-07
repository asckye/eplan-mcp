using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters.Generated;
using EplanMcp.Contracts;
using EplanInsert = Eplan.EplApi.HEServices.Insert;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads the page a window macro goes on, and inserts the macro the way spike S10 did in the dispatcher (api-notes A14):
/// <c>WindowMacro.Open</c>, then <c>Insert.WindowMacro(macro, representation, variant, page, point, MoveKind.Absolute,
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
        LayoutGuard.Model? layout = null;
        try
        { layout = EplanLayout.Read(projectPath, pageName); }
        catch (BridgeErrorException ex) when (ex.Error.Reason == ErrorReason.CapabilityMissing)
        { WriteExceptionDiagnostics.Report(ex); }
        return new MacroTargetPage
        {
            Name = page.Name,
            Type = page.PageType.ToString(),
            Width = size?.X,
            Height = size?.Y,
            Layout = layout,

            // Every placement on the page, groups resolved (EPLAN XML docs, Page.AllPlacements).
            Objects = [.. EplanNativePreconditions.Valid(page.AllPlacements).ToArray().Select(placement => placement.ToStringIdentifier())],
        };
    }

    public PlacedMacro Insert(string projectPath, string pageName, string macroPath, int variant, double x, double y, bool number, string representation = "MultiLine", string? part = null, string? partVariant = null, bool macroBox = true, IReadOnlyDictionary<string, string>? values = null)
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
                (WindowMacro.Enums.RepresentationType)Enum.Parse(typeof(WindowMacro.Enums.RepresentationType), representation),
                variant,
                page,
                new PointD(x, y),
                EplanInsert.MoveKind.Absolute,
                number ? WindowMacro.Enums.NumerationMode.Number : WindowMacro.Enums.NumerationMode.None);
        }
        catch (BaseException ex) when (ex.Message.IndexOf(StackedSymbols, StringComparison.Ordinal) >= 0)
        {
            WriteExceptionDiagnostics.Report(ex);
            var stacked = new BridgeErrorException(
                ErrorReason.InvalidArgument,
                "EPLAN refuses to place the macro there because the same symbols are at that position already, so nothing was inserted. Choose another position; eplan_page_get shows the devices on the page.",
                ex);
            stacked.Error.Metadata = new Dictionary<string, string> { ["page"] = pageName };
            throw stacked;
        }

        if (values is not null)
        {
            foreach (KeyValuePair<string, string> value in values.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                try
                {
                    PlaceHolder[] holders = [.. placed.OfType<PlaceHolder>().Where(holder => string.Equals(holder.Name, value.Key, StringComparison.Ordinal))];
                    if (holders.Length != 1)
                    {
                        throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The inserted macro must contain exactly one placeholder named '" + value.Key + "' to apply its value set; nothing was committed.");
                    }

                    PlaceHolderApi.ApplyRecord(holders[0], value.Value);
                }
                catch (Exception failure) when (failure is not BridgeErrorException)
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not apply the value set to inserted placeholder '" + value.Key + "'; nothing was committed.", failure);
                }
            }
        }

        int removed = 0;
        if (!macroBox)
        {
            foreach (MacroBox box in placed.OfType<MacroBox>())
            { box.Remove(); removed++; }
        }
        Function[] added = [.. Functions(project, page).Where(f => !before.Contains(f.ToStringIdentifier()))];
        if (part is not null)
        {
            // A82: retain existing macro articles; assign only to new main functions in this same transaction.
            foreach (Function function in added.Where(f => f.IsMainFunction && f.ArticleReferences.Length == 0))
            {
                function.AddArticleReference(part, partVariant ?? string.Empty, 1);
                if (!function.ArticleReferences.Any(a => a.PartNr == part && a.VariantNr == (partVariant ?? string.Empty) && a.Count == 1))
                { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The inserted main function's part reference differs from the requested part; nothing was committed."); }
                GC.KeepAlive(function);
            }
        }
        return Read(added, placed.Length - removed);
    }

    public PlacedMacro ReadCommitted(string projectPath, string pageName, PlacedMacro placed)
    {
        MainThread.Assert();
        // A79: carry only managed object IDs across Commit/Dispose, then obtain fresh native wrappers.
        Project project = EplanProjects.ByPath(projectPath);
        Page page = Find(project, pageName) ?? throw new InvalidOperationException("The inserted macro's page is gone.");
        var ids = new HashSet<string>(placed.ObjectIds, StringComparer.Ordinal);
        Function[] functions = [.. Functions(project, page).Where(f => ids.Contains(f.ToStringIdentifier()))];
        if (functions.Length != ids.Count)
        { throw new InvalidOperationException("Some inserted macro functions could not be read after commit."); }
        return Read(functions, placed.ObjectCount);
    }

    private static PlacedMacro Read(Function[] functions, int objectCount) =>
        new()
        {
            ObjectIds = [.. functions.Select(f => f.ToStringIdentifier())],
            Functions =
            [
                .. functions.Select(f => new MacroFunctionRecord
                {
                    Name = f.Name ?? string.Empty,
                    VisibleName = f.VisibleName ?? string.Empty,
                    Category = f.FunctionCategory.ToString(),
                    Main = f.IsMainFunction,
                }),
            ],
            ArticleReferences = [.. functions.Where(f => f.IsMainFunction).SelectMany(f => f.ArticleReferences.Select(a => new MacroPartReference
            { Tag = f.Name ?? string.Empty, PartNumber = a.PartNr ?? string.Empty, Variant = a.VariantNr ?? string.Empty, Count = a.Count }))],
            ObjectCount = objectCount,
        };

    /// <summary>The page whose full name is exactly <paramref name="pageName"/>: EPLAN's exact name filter (S6), checked ordinally.</summary>
    private static Page? Find(Project project, string pageName)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        return EplanNativePreconditions.Valid(finder.GetPages(filter)).ToArray().FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
    }

    /// <summary>The functions on the page: <c>FunctionsFilter.Page</c>, not the shared <c>Page.Filter</c> (BannedSymbols.txt).</summary>
    private static Function[] Functions(Project project, Page page)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new FunctionsFilter { Page = page };
        return EplanNativePreconditions.Valid(finder.GetFunctions(filter)).ToArray();
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
        catch (InvalidOperationException nativeFailure)
        {
            WriteExceptionDiagnostics.Report(nativeFailure);
            return null;
        }
        catch (BaseException nativeFailure)
        {
            WriteExceptionDiagnostics.Report(nativeFailure);
            return null;
        }
    }
}
