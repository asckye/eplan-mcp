using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
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

    public MacroContent ReadMacro(string projectPath, string macroPath)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);

        // Opening reads the file only: S10 saw no change to the project's files or functions (api-notes A14).
        using var macro = new WindowMacro();
        macro.Open(macroPath, project);
        Placement[] objects = macro.Objects;
        PointD size = macro.Size;
        return new MacroContent
        {
            Representations = [.. macro.RepresentationTypes.Select(t => new KeyValuePair<string, List<int>>(t.ToString(), [.. macro.GetVariants(t)]))],
            Width = size.X,
            Height = size.Y,
            Functions =
            [
                .. objects.OfType<Function>().Select(f => new MacroFunctionRecord
                {
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
