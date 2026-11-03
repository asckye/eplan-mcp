using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Appends references to a unique main function without retaining native objects across requests or commit.</summary>
internal sealed class EplanDevicePartAssigner : IDevicePartAssigner
{
    public IReadOnlyList<DevicePartTarget> ReadMainFunctions(string projectPath, string tag)
    {
        MainThread.Assert();
        var targets = new List<DevicePartTarget>();
        foreach (Function function in Find(EplanProjects.ByPath(projectPath)).Where(f => f.IsMainFunction))
        {
            // A82: a PLC main function's name can include :1; match its device tag instead.
            string full;
            using (PropertyValue value = function.Properties.FUNC_IDENTDEVICETAG)
            { full = value.IsEmpty ? string.Empty : value.ToString(); }
            if (!string.Equals(full, tag, StringComparison.Ordinal)
                && !string.Equals(DeviceTags.Shown(function.VisibleName), DeviceTags.Shown(tag), StringComparison.OrdinalIgnoreCase))
            { continue; }
            targets.Add(new DevicePartTarget
            {
                Id = function.ToStringIdentifier(),
                Name = function.Name,
                Tag = full,
                VisibleName = function.VisibleName,
                Page = function.Page?.Name,
                Parts = [.. function.ArticleReferences.Select(r => new DevicePart { PartNumber = r.PartNr ?? string.Empty, Variant = r.VariantNr, Count = r.Count })],
            });
            GC.KeepAlive(function);
        }
        return targets;
    }

    public void AddPart(string projectPath, string mainId, string part, string variant)
    {
        MainThread.Assert();
        Function main = Find(EplanProjects.ByPath(projectPath)).Single(f => f.IsMainFunction && f.ToStringIdentifier() == mainId);
        // A82: AddArticleReference appends the checked part and is read back through ArticleReferences.
        main.AddArticleReference(part, variant, 1);
        GC.KeepAlive(main);
    }

    private static Function[] Find(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        // A12: finder can omit extra main representations; count page placements too, once per native object.
        return [.. EplanNativePreconditions.Valid(finder.GetFunctions(null)).Concat(project.Pages
                .SelectMany(page => EplanNativePreconditions.Valid(page.AllPlacements).OfType<Function>()))
            .GroupBy(function => function.ObjectIdentifier).Select(group => group.First()).OrderBy(function => function.ObjectIdentifier)];
    }
}
