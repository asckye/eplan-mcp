using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Renames devices the way spike S9 renamed one completely (api-notes A13): every function named the old tag, the main
/// function first, is given the new name by assigning <c>Function.Name</c>. With a name filter EPLAN also returns
/// further main functions under a device's tag (black box, second representation, A12); they are renamed too, and the
/// one with a shown tag stands for the device. Read-backs list all functions without a filter, as S9 did inside the
/// SafetyPoint. Main thread; <see cref="Rename"/> inside the write's TransactionScope.
/// </summary>
internal sealed class EplanDeviceRenamer : IDeviceRenamer
{
    public DeviceNames? ReadDevice(string projectPath, string tag)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Function[] named = Named(finder, tag);
        if (Main(named) is not { } main)
        {
            return null;
        }

        // The tag without structure, e.g. KF1 or EC1-MA2 (spike S6).
        PropertyValue own = main.Properties.FUNC_DEVICETAG_FULLNAME;
        return new DeviceNames
        {
            Tag = tag,
            DeviceTag = own.IsEmpty ? string.Empty : own.ToString(),
            Functions = [.. named.Select(f => new DeviceNames.NamedFunction { Id = f.ToStringIdentifier(), IsMain = f.IsMainFunction, Page = f.Page?.Name })],
            SubFunctions = [.. main.SubFunctions.Select(f => f.Name ?? string.Empty)],
        };
    }

    public IReadOnlyList<string> FunctionNames(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return [.. finder.GetFunctions(null).Select(f => f.Name ?? string.Empty)];
    }

    public RenameReadBack Rename(string projectPath, string tag, string newTag)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Function[] named = Named(finder, tag);
        Function main = Main(named) ?? throw new InvalidOperationException("The planned device has no main function any more.");
        main.Name = newTag;
        foreach (Function function in named)
        {
            if (!ReferenceEquals(function, main))
            {
                function.Name = newTag;
            }
        }

        string renamed = main.Name ?? string.Empty;
        string[] names = [.. finder.GetFunctions(null).Select(f => f.Name ?? string.Empty)];
        return new RenameReadBack
        {
            MainName = renamed,
            VisibleName = main.VisibleName ?? string.Empty,
            Remaining = names.Count(name => string.Equals(name, tag, StringComparison.Ordinal)),
            Renamed = names.Count(name => string.Equals(name, renamed, StringComparison.Ordinal)),
        };
    }

    /// <summary>Every function named exactly <paramref name="tag"/>: the exact name filter (S6), checked ordinally.</summary>
    private static Function[] Named(DMObjectsFinder finder, string tag)
    {
        using var filter = new FunctionsFilter();
        filter.Name = tag;
        filter.ExactNameMatching = true;
        return [.. finder.GetFunctions(filter).Where(f => string.Equals(f.Name, tag, StringComparison.Ordinal))];
    }

    /// <summary>The main function that stands for the device: the one with a shown tag, else the first (A12).</summary>
    private static Function? Main(Function[] named) =>
        named.FirstOrDefault(f => f.IsMainFunction && DeviceTags.Shown(f.VisibleName) is not null) ?? named.FirstOrDefault(f => f.IsMainFunction);
}
