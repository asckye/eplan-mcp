using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Renames the complete device through NameService (A85), preserving connection-point suffixes.
/// Main thread; Rename runs in the transaction, ReadDevice reacquires committed objects by their new tag.
/// </summary>
internal sealed class EplanDeviceRenamer : IDeviceRenamer
{
    public DeviceNames? ReadDevice(string projectPath, string tag)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Function[] named = Named(finder, tag);
        if (MainFunction(named) is not { } main)
        {
            return null;
        }

        // The tag without structure, e.g. KF1 or EC1-MA2 (spike S6).
        PropertyValue own = main.Properties.FUNC_DEVICETAG_FULLNAME;
        return new DeviceNames
        {
            Tag = tag,
            DeviceTag = own.IsEmpty ? string.Empty : own.ToString(),
            VisibleName = main.VisibleName ?? string.Empty,
            Functions = [.. named.Select(f => new DeviceNames.NamedFunction { Name = f.Name ?? string.Empty, Id = f.ToStringIdentifier(), IsMain = f.IsMainFunction, Page = f.Page?.Name })],
            SubFunctions = [.. EplanNativePreconditions.Valid(main.SubFunctions).Select(f => f.Name ?? string.Empty)],
        };
    }

    public IReadOnlyList<string> FunctionNames(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return [.. EplanNativePreconditions.Valid(finder.GetFunctions(null)).ToArray().Select(f => f.Name ?? string.Empty)];
    }

    public RenameReadBack Rename(string projectPath, string tag, string newTag)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        Function[] named = Named(finder, tag);
        Function main = MainFunction(named) ?? throw new InvalidOperationException("The planned device has no main function any more.");
        using var parsed = new FunctionPropertyList();
        using var additional = new FunctionPropertyList();
        using var newName = new FunctionBasePropertyList();
        project.ParseNameAndProps((int)main.FunctionCategory, newTag, parsed, additional);
        using PropertyValue plant = parsed.DESIGNATION_PLANT;
        using PropertyValue location = parsed.DESIGNATION_LOCATION;
        using PropertyValue code = parsed.FUNC_CODE;
        using PropertyValue counter = parsed.FUNC_COUNTER;
        newName.DESIGNATION_PLANT = plant;
        newName.DESIGNATION_LOCATION = location;
        newName.FUNC_CODE = code;
        newName.FUNC_COUNTER = counter;
        using var service = new NameService();
        service.RenameDevice(main, newName, false, true);
        return new RenameReadBack { MainName = main.Name ?? string.Empty, VisibleName = main.VisibleName ?? string.Empty };
    }

    /// <summary>Every function carrying the device tag, including its connection-point suffixes.</summary>
    private static Function[] Named(DMObjectsFinder finder, string tag)
    {
        return [.. EplanNativePreconditions.Valid(finder.GetFunctions(null)).Where(f => f.Name == tag || (f.Name ?? string.Empty).StartsWith(tag + ":", StringComparison.Ordinal))];
    }

    /// <summary>The main function that stands for the device: the one with a shown tag, else the first (A12).</summary>
    private static Function? MainFunction(Function[] named) =>
        named.FirstOrDefault(f => f.IsMainFunction && DeviceTags.Shown(f.VisibleName) is not null) ?? named.FirstOrDefault(f => f.IsMainFunction);
}
