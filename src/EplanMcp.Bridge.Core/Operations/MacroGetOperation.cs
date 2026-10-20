using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.MacroGet"/> (docs/tools/eplan_macro_get.md): checks the macro's path (SEC-19), opens the
/// macro in the project without inserting it, and lists its functions other than wires, one per tag.
/// </summary>
public sealed class MacroGetOperation(IProjectCatalog catalog, IMacroReader reader) : Operation<MacroGetArgs, MacroGetData>
{
    /// <summary>The most devices one result lists (tool spec 3).</summary>
    public const int MaxDevices = 50;

    /// <summary>The <c>FunctionCategory</c> of a connection line, which is no device.</summary>
    internal const string Wire = "Wire";

    public override string Name => OperationNames.MacroGet;

    protected override MacroGetData Execute(MacroGetArgs args)
    {
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string root = reader.MacroRoot() is { } resolved && Directory.Exists(resolved)
            ? resolved
            : throw new BridgeErrorException(ErrorReason.EplanOperationFailed, MacroListOperation.NoMacroDirectory);
        string path = MacroFiles.Resolve(root, args.Macro);
        MacroContent content = reader.ReadMacro(project.Path, path);

        // One entry per tag: a terminal strip or a cable appears once however many functions carry its tag; the one the
        // drawing labels wins, as in the device lists.
        List<MacroDevice> devices =
        [
            .. content.Functions
                .Where(f => f.Category != Wire && f.Name.Length > 0)
                .GroupBy(f => f.Name, StringComparer.Ordinal)
                .Select(g => g.FirstOrDefault(f => f.VisibleName.Length > 0) ?? g.First())
                .Select(f => new MacroDevice { Tag = f.Name, VisibleTag = f.VisibleName.Length == 0 ? null : f.VisibleName, Category = f.Category, Main = f.Main }),
        ];
        return new MacroGetData
        {
            Project = project.Name,
            Path = MacroFiles.Normalized(args.Macro),
            Representations = [.. content.Representations.Select(r => new MacroRepresentation { Type = r.Key, Variants = r.Value })],
            Width = content.Width,
            Height = content.Height,
            Devices = [.. devices.Take(MaxDevices)],
            DeviceCount = devices.Count,
            ObjectCount = content.ObjectCount,
        };
    }
}
