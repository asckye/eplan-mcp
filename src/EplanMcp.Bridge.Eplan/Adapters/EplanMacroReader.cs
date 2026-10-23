using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core.Operations;

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

    public MacroContent ReadMacro(string projectPath, string macroPath, bool defaultMultiLine = false)
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
        if (defaultMultiLine && representations.Any(r => r.Key == "MultiLine" && r.Value.Contains(0)))
        {
            macro.Open(macroPath, project, WindowMacro.Enums.RepresentationType.MultiLine, 0, false);
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
            Functions =
            [
                .. objects.OfType<Function>().Select(f => new MacroFunctionRecord
                {
                    Kind = f is Terminal ? "terminal" : f is PLC ? "plc" : f is Cable ? "cable" : "function",
                    Name = f.Name ?? string.Empty,
                    VisibleName = f.VisibleName ?? string.Empty,
                    Category = f.FunctionCategory.ToString(),
                    Main = f.IsMainFunction,
                }),
            ],
            ObjectCount = objects.Length,
        };
    }
}
