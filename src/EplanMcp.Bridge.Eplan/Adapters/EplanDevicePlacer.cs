using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Places a part's device the way spike S20 did in the dispatcher (api-notes A26):
/// <c>new DeviceService().CreateDevice(part, variant, page, point)</c>, which uses the part's macro or function templates
/// (A82), including persistent unplaced functions. A85 renames the whole device with NameService before commit;
/// committed readback reacquires every function by its new tag because unplaced IDs change (A79/A80/A85).
/// </summary>
internal sealed class EplanDevicePlacer : IDevicePlacer
{
    public MacroTargetPage? ReadPage(string projectPath, string pageName)
    {
        MacroTargetPage? result = new EplanMacroInserter().ReadPage(projectPath, pageName);
        if (result is not null)
        {
            result.PredictDevice = args => EplanLayout.Device(projectPath, args, result.Layout!);
            foreach (LocationBox box in EplanNativePreconditions.Valid(Find(EplanProjects.ByPath(projectPath), pageName)!.AllPlacements).OfType<LocationBox>())
            {
                using FunctionBasePropertyList parts = box.NameParts;
                using PropertyValue location = parts.DESIGNATION_LOCATION;
                result.LocationBoxStructures.Add(location.IsEmpty ? string.Empty : "+" + location.ToString());
            }
        }
        return result;
    }

    public PlacedDevice Place(string projectPath, string pageName, string part, string variant, double x, double y, string tag, DevicePlaceArgs? options = null)
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
            WriteExceptionDiagnostics.Report(ex);
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                $"EPLAN could not place part '{part}', so nothing was changed. metadata.eplanMessage has EPLAN's message; check the part's macro in EPLAN's parts management, or insert a window macro with eplan_macro_insert.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["part"] = part, ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }

        Function? main = created.FirstOrDefault(f => f.IsMainFunction) ?? created.OfType<Terminal>().FirstOrDefault();
        TerminalPlacementData? terminalData = null;
        if (main is not null && options?.Library is { } library)
        {
            var replacement = project.SymbolLibraries.Single(l => string.Equals(l.Name, library, StringComparison.OrdinalIgnoreCase))
                .Symbols.Single(s => string.Equals(s.Name, options.Symbol, StringComparison.OrdinalIgnoreCase)).Variants.Single(v => v.VariantNr == options.SymbolVariant);
            main.SetSymbolVariant(replacement, false);
        }
        if (main is Terminal terminal && options?.TerminalStrip is not null)
        {
            var symbol = new SymbolSearchItem { Library = terminal.SymbolVariant.SymbolLibraryName, Name = terminal.SymbolVariant.SymbolName, Variant = terminal.SymbolVariant.VariantNr, FunctionCategory = "Terminal" };
            terminalData = new EplanDeviceSymbolPlacement(projectPath, pageName, symbol).CreateTerminal(options, tag, project, page, terminal.SymbolVariant, terminal).Terminal;
        }
        else if (tag.Length > 0 && main is not null)
        {
            using var parsed = new FunctionPropertyList();
            using var additional = new FunctionPropertyList();
            using var newName = new FunctionBasePropertyList();
            project.ParseNameAndProps((int)main.FunctionCategory, tag, parsed, additional);
            using PropertyValue plant = parsed.DESIGNATION_PLANT;
            using PropertyValue location = parsed.DESIGNATION_LOCATION;
            using PropertyValue code = parsed.FUNC_CODE;
            using PropertyValue counter = parsed.FUNC_COUNTER;
            // A tag without a plant or location parses to an empty value, which the list refuses (S063111).
            if (!plant.IsEmpty)
            { newName.DESIGNATION_PLANT = plant; }
            if (!location.IsEmpty)
            { newName.DESIGNATION_LOCATION = location; }
            newName.FUNC_CODE = code;
            newName.FUNC_COUNTER = counter;
            using var service = new NameService();
            service.RenameDevice(main, newName, false, true);
        }

        var result = new PlacedDevice { OnPage = OnPage(project, page).Count(id => !before.Contains(id)), Terminal = terminalData };
        Read(created, result);
        return result;
    }

    public void ReadCommitted(string projectPath, PlacedDevice result)
    {
        MainThread.Assert();
        string tag = result.Category == "Terminal" ? result.MainName : result.MainName.Split(':')[0];
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        IEnumerable<Function> functions = result.Category == "Terminal"
            ? EplanNativePreconditions.Valid(finder.GetTerminals(null)) : EplanNativePreconditions.Valid(finder.GetFunctions(null));
        Read([.. functions
            .Where(f => f.Name == tag || result.Category != "Terminal" && f.Name.StartsWith(tag + ":", StringComparison.Ordinal))], result);
    }

    private static void Read(Function[] functions, PlacedDevice result)
    {
        Function[] ordered = [.. functions.Where(f => f.IsMainFunction), .. functions.Where(f => !f.IsMainFunction)];
        Function? main = ordered.FirstOrDefault(f => f.IsMainFunction) ?? ordered.OfType<Terminal>().FirstOrDefault();
        result.Names = [.. ordered.Select(f => f.Name ?? string.Empty)];
        result.MainName = main?.Name ?? string.Empty;
        result.VisibleName = main?.VisibleName ?? string.Empty;
        result.Category = main?.FunctionCategory.ToString() ?? string.Empty;
        result.Unplaced = functions.Count(f => !f.IsPlaced);
        result.UnplacedFunctions = [.. ordered.Where(f => !f.IsPlaced).Select(f => f.Name ?? string.Empty)];
    }

    /// <summary>The page whose full name is exactly <paramref name="pageName"/>: EPLAN's exact name filter (S6), checked ordinally.</summary>
    private static Page? Find(Project project, string pageName)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        return EplanNativePreconditions.Valid(finder.GetPages(filter)).ToArray().FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
    }

    /// <summary>The session identifiers of the functions on the page: <c>FunctionsFilter.Page</c>, not the shared <c>Page.Filter</c>.</summary>
    private static string[] OnPage(Project project, Page page)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new FunctionsFilter { Page = page };
        return [.. EplanNativePreconditions.Valid(finder.GetFunctions(filter)).ToArray().Select(f => f.ToStringIdentifier())];
    }
}
