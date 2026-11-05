using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Appends references to a unique part target without retaining native objects across requests or commit.</summary>
internal sealed class EplanDevicePartAssigner : IDevicePartAssigner
{
    public IReadOnlyList<DevicePartTarget> ReadMainFunctions(string projectPath, string tag, bool includeTerminals = false)
    {
        MainThread.Assert();
        var targets = new List<DevicePartTarget>();
        foreach (Function function in Find(EplanProjects.ByPath(projectPath)).Where(f => f.IsMainFunction || (includeTerminals && f.FunctionCategory.ToString() == "Terminal" && f.Name == tag)))
        {
            // A82: a PLC main function's name can include :1; match its device tag instead.
            string full = function.Name;
            if (function.IsMainFunction)
            {
                using PropertyValue value = function.Properties.FUNC_IDENTDEVICETAG;
                full = value.IsEmpty ? string.Empty : value.ToString();
            }
            if (!(includeTerminals && function.FunctionCategory.ToString() == "Terminal" && function.Name == tag) && !string.Equals(full, tag, StringComparison.Ordinal)
                && !string.Equals(DeviceTags.Shown(function.VisibleName), DeviceTags.Shown(tag), StringComparison.OrdinalIgnoreCase))
            { continue; }
            targets.Add(new DevicePartTarget
            {
                IsMain = function.IsMainFunction,
                Category = function.FunctionCategory.ToString(),
                Id = function.ToStringIdentifier(),
                Name = function.Name,
                Tag = function.IsMainFunction ? full : function.Name,
                VisibleName = function.VisibleName,
                Page = function.Page?.Name,
                Parts = [.. function.ArticleReferences.Select(r => new DevicePart { PartNumber = r.PartNr ?? string.Empty, Variant = r.VariantNr, Count = r.Count })],
            });
            GC.KeepAlive(function);
        }
        return targets;
    }

    public void AddPart(string projectPath, string mainId, string part, string variant, int count = 1)
    {
        MainThread.Assert();
        Function main = Find(EplanProjects.ByPath(projectPath)).Single(f => (f.IsMainFunction || f.FunctionCategory.ToString() == "Terminal") && f.ToStringIdentifier() == mainId);
        // A82: AddArticleReference appends the checked part and is read back through ArticleReferences.
        main.AddArticleReference(part, variant, checked((uint)count));
        GC.KeepAlive(main);
    }

    public ITransactionScopeFactory PartTransactions(string projectPath) => new PartFactory(projectPath);

    private sealed class PartFactory(string project) : ITransactionScopeFactory
    { public ITransactionScope Begin(string undoDescription) => new FunctionTransactionScope(EplanProjects.ByPath(project), undoDescription, undoCommitted: true); }

    public void TransferPartData(string projectPath, string functionId)
    {
        MainThread.Assert();
        Function function = Find(EplanProjects.ByPath(projectPath)).Single(f => f.ToStringIdentifier() == functionId);
        using Transaction transaction = new TransactionManager().CreateTransaction();
        using var devices = new DeviceService();
        // A93: a reference alone leaves the part's definition and technical data out of the function.
        if (!devices.UpdateDevice(function))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN found no part reference to transfer data from."); }
        transaction.Commit();
        GC.KeepAlive(function);
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
