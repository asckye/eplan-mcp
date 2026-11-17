using System.Diagnostics;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A bounded read inside invoke; also callable directly within a later guard's main-thread call.</summary>
public sealed class PlanNamesOperation(IProjectCatalog catalog, IPlanNameReader reader, Func<long>? milliseconds = null)
    : Operation<PlanNamesArgs, PlanNamesData>
{
    public const int MaxNames = 250_000;
    public const int MaxJsonBytes = 8 * 1024 * 1024;
    public override string Name => OperationNames.PlanNames;

    protected override PlanNamesData Execute(PlanNamesArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Project))
        {
            throw InvalidArgument("'project' must name an open project. Call eplan_project_list to choose one.");
        }

        var clock = Stopwatch.StartNew();
        Func<long> now = milliseconds ?? (() => clock.ElapsedMilliseconds);
        long start = now();
        void Checkpoint()
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (now() - start >= 10_000)
            {
                throw new BridgeErrorException(ErrorReason.Timeout, "Name enumeration exceeded 10 s. Narrow the plan or use a smaller project.", retryable: true);
            }
        }

        Checkpoint();
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        PlanNameInventory inventory = reader.ReadNames(project.Path, Checkpoint);
        PlanNamesData data = Build(project, inventory, Checkpoint);
        Checkpoint();
        return data;
    }

    internal static PlanNamesData Build(OpenProject project, PlanNameInventory inventory, Action checkpoint,
        int maxNames = MaxNames, int maxJsonBytes = MaxJsonBytes)
    {
        var data = new PlanNamesData { Project = project.Name, ProjectPath = project.Path, ProjectGuid = inventory.ProjectGuid };
        var names = new HashSet<(string Kind, string Name)>();
        long bytes = 2;
        IEnumerable<(string Kind, string Name)> entries = inventory.Pages.Select(p => ("page", p))
            .Concat(inventory.Functions.Where(f => f.Name.Length > 0).Select(f => ("function", f.Name)));
        foreach (var entry in entries)
        {
            checkpoint();
            if (!names.Add(entry))
            {
                continue;
            }

            var item = new PlanNameEntry { Kind = entry.Kind, Name = entry.Name };
            bytes += BridgeJson.ToUtf8(BridgeJson.FromObject(item)).Length + (names.Count == 1 ? 0 : 1);
            if (names.Count > maxNames || bytes > maxJsonBytes)
            {
                return data;
            }
        }

        data.Names = [.. names.OrderBy(n => n.Kind, StringComparer.Ordinal).ThenBy(n => n.Name, StringComparer.Ordinal)
            .Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name })];
        data.NamesHash = PlanNamesDigest.Names(project.Path, data.Names);
        data.Complete = true;
        return data;
    }
}
