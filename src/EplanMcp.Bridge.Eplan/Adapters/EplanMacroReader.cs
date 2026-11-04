using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The macro directory through EPLAN's path variables, and window macros opened without inserting them. Main thread,
/// inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanMacroReader : IMacroReader
{
    public string? MacroRoot()
    {
        MainThread.Assert();

        // A variable EPLAN cannot resolve comes back unchanged (api-notes A4: $(MD_MACROS) resolves in 2026).
        string resolved = PathMap.SubstitutePath("$(MD_MACROS)");
        return string.IsNullOrWhiteSpace(resolved) || resolved.Contains("$(") ? null : resolved;
    }

    public PartMacroSource ResolvePartMacro(string part, string? partVariant)
    {
        MainThread.Assert();
        // A82: open only the configured database; never select, upgrade or modify it.
        using MDPartsDatabase database = new MDPartsManagement().OpenDatabase();
        try
        {
            MDPart[] variants = database.GetPartVariants(part) ?? [];
            if (variants.Length == 0)
            { variants = [.. database.Parts.Where(p => string.Equals(p.PartNr, part, StringComparison.OrdinalIgnoreCase))]; }
            string[] numbers = [.. variants.Select(p => p.PartNr).Distinct(StringComparer.Ordinal)];
            if (numbers.Length == 0)
            { throw new BridgeErrorException(ErrorReason.PartNotFound, "The parts database has no such part; use eplan_part_search to find its number."); }
            if (numbers.Length > 1)
            { throw ProjectResolver.Ambiguous("part", part, numbers, "Several parts differ only in letter case; pass an exact part number."); }
            MDPart? selected = partVariant is null
                ? variants.FirstOrDefault(p => p.Variant == "1") ?? (variants.Length == 1 ? variants[0] : null)
                : variants.FirstOrDefault(p => string.Equals(p.Variant, partVariant, StringComparison.Ordinal));
            if (selected is null)
            { throw WriteArguments.Invalid([$"'part_variant' must select a variant of {numbers[0]}; available variants: {string.Join(", ", variants.Select(p => p.Variant))}."]); }
            string macro = selected.Properties.ARTICLE_GROUPSYMBOLMACRO.ToString()?.Trim() ?? string.Empty;
            GC.KeepAlive(selected);
            if (macro.Length > 0)
            {
                macro = PathMap.SubstitutePath(macro);
                if (Path.IsPathRooted(macro))
                {
                    string prefix = (MacroRoot() ?? string.Empty).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    if (prefix.Length <= 1 || !macro.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    { throw WriteArguments.Invalid(["The part's group symbol macro must be inside the configured macro directory."]); }
                    macro = macro.Substring(prefix.Length);
                }
            }
            return new PartMacroSource { Part = selected.PartNr, PartVariant = selected.Variant ?? string.Empty, Macro = macro };
        }
        finally { database.Close(); }
    }

    public MacroContent ReadMacro(string projectPath, string macroPath, bool defaultMultiLine = false, string? representation = null, int variant = 0)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);

        // Opening reads the file only: S10 saw no change to the project's files or functions (api-notes A14).
        using var macro = new WindowMacro();
        if (defaultMultiLine)
        {
            macro.Open(macroPath, project, false);
        }
        else
        {
            macro.Open(macroPath, project);
        }
        List<KeyValuePair<string, List<int>>> representations = [.. macro.RepresentationTypes
            .Select(t => new KeyValuePair<string, List<int>>(t.ToString(), [.. macro.GetVariants(t)]))];
        string? selected = representation ?? (defaultMultiLine ? "MultiLine" : variant != 0 ? macro.RepresentationType.ToString() : null);
        if (selected is not null && representations.Any(r => r.Key == selected && r.Value.Contains(variant)))
        {
            macro.Open(macroPath, project, (WindowMacro.Enums.RepresentationType)Enum.Parse(typeof(WindowMacro.Enums.RepresentationType), selected), variant, false);
        }
        Placement[] objects = macro.Objects;
        PointD size = macro.Size;
        return new MacroContent
        {
            Complete = true,
            Representation = macro.RepresentationType.ToString(),
            Variant = macro.Variant,
            FunctionCount = objects.OfType<Function>().Count(),
            Representations = representations,
            Width = size.X,
            Height = size.Y,
            ReferencePoint = Point(() => macro.ReferencePoint),
            Location = Point(() => macro.Location),
            Functions =
            [
                .. objects.OfType<Function>().Select(f => new MacroFunctionRecord
                {
                    Kind = f is Terminal ? "terminal" : f is PLC ? "plc" : f is Cable ? "cable" : "function",
                    Name = f.Name ?? string.Empty,
                    VisibleName = f.VisibleName ?? string.Empty,
                    Category = f.FunctionCategory.ToString(),
                    Main = f.IsMainFunction,
                    Location = Point(() => f.Location),
                }),
            ],
            ObjectCount = objects.Length,
            MacroBoxCount = objects.OfType<MacroBox>().Count(),
        };
    }

    private static double[]? Point(Func<PointD> read)
    {
        try
        {
            PointD point = read();
            if (double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Y) || double.IsInfinity(point.Y))
            { throw new InvalidOperationException("Macro coordinates are not finite."); }
            return [point.X, point.Y];
        }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException)
        { WriteExceptionDiagnostics.Report(ex); return null; }
    }
}
