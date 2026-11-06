using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
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
        if (Representative(named) is not { } main)
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
            Functions = [.. named.Select(f => new DeviceNames.NamedFunction { Name = f.Name ?? string.Empty, Id = f.ToStringIdentifier(), IsMain = f.IsMainFunction, Category = Category(f), Page = f.Page?.Name })],
            SubFunctions = [.. EplanNativePreconditions.Valid(main.IsMainFunction ? main.SubFunctions : []).Select(f => f.Name ?? string.Empty)],
        };
    }

    public IReadOnlyList<string> FunctionNames(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return [.. EplanNativePreconditions.Valid(finder.GetFunctions(null)).ToArray().Select(f => f.Name ?? string.Empty)];
    }

    private static string? Category(Function function)
    {
        try
        { return function.FunctionCategory.ToString(); }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException)
        { WriteExceptionDiagnostics.Report(ex); return null; }
    }

    public RenameReadBack Rename(string projectPath, string tag, string newTag)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        Function[] named = Named(finder, tag);
        Function main = Representative(named) ?? throw new InvalidOperationException("The planned device has no main function or terminal-only representative any more.");
        bool hasMain = named.Any(f => f.IsMainFunction) || Named(finder, newTag).Any(f => f.IsMainFunction);
        EplanDeviceRename.Apply(project, main, newTag);
        // PDF redraw 2026-11-06: a macro's PLC box without a main function shows -?K1 and takes no part; with a real tag and no other box it is the device.
        if (!hasMain && named.FirstOrDefault(f => Category(f) == "PLCBox") is { } box)
        { box.IsMainFunction = true; }
        return new RenameReadBack { MainName = main.Name ?? string.Empty, VisibleName = main.VisibleName ?? string.Empty };
    }

    /// <summary>Every function carrying the device tag, including its connection-point suffixes.</summary>
    private static Function[] Named(DMObjectsFinder finder, string tag)
    {
        return [.. EplanNativePreconditions.Valid(finder.GetFunctions(null)).Where(f => f.Name == tag || (f.Name ?? string.Empty).StartsWith(tag + ":", StringComparison.Ordinal))];
    }

    /// <summary>The main function (A12), or the first point of a terminal-only PLC module (A89).</summary>
    private static Function? Representative(Function[] named) =>
        named.FirstOrDefault(f => f.IsMainFunction && DeviceTags.Shown(f.VisibleName) is not null) ?? named.FirstOrDefault(f => f.IsMainFunction)
        ?? (named.Length > 0 && named.All(f => !f.IsMainFunction && Category(f) is "PLCTerminal" or "PLCBox") ? named.FirstOrDefault(f => Category(f) == "PLCTerminal") ?? named[0] : null);
}
