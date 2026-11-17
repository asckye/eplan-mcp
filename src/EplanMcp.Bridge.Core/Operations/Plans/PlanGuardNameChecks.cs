using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Names are metadata, not detail objects. One fresh read serves absent checks and the rename closure.</summary>
internal sealed class PlanGuardNameChecks(IReadOnlyList<PlanFunction> functions)
{
    public static PlanGuardNameChecks Read(IPlanFunctionReader reader, OpenProject project, IEnumerable<string> candidates,
        IReadOnlyList<KeyValuePair<long, string>> pages, Action checkpoint)
    {
        string[] requested = [.. candidates.Distinct(StringComparer.Ordinal)];
        if (requested.Length == 0)
        {
            return new([]);
        }

        checkpoint();
        IReadOnlyList<PlanFunction> found = reader is IPlanGuardFunctionReader guarded
            ? guarded.ReadForGuard(project.Path, requested, checkpoint) : reader.Read(project.Path);
        checkpoint();
        // Keep the independent LP-2 name limits; never apply the 10,000 content-object cap to this inventory.
        var inventory = new PlanNameInventory();
        inventory.Pages.AddRange(pages.Select(p => p.Value));
        inventory.Functions.AddRange(found);
        if (!PlanNamesOperation.Build(project, inventory, checkpoint).Complete)
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.CapabilityMissing,
                "The guard name inventory exceeds its resource limits; use a smaller project before writing.", retryable: false,
                new Dictionary<string, string> { ["missing"] = "plan.names:complete" }));
        }

        checkpoint();
        return new(found);
    }

    public bool Occupied(string candidate) => functions.Any(f => f.Name.Length > 0 && Collides(f.Name, candidate));

    internal static bool Collides(string occupied, string candidate) =>
        string.Equals(occupied, candidate, StringComparison.OrdinalIgnoreCase)
        || occupied.StartsWith(candidate + ":", StringComparison.OrdinalIgnoreCase);

    public bool ClosureMatches(PlanStepGuardArgs guard, string deviceName)
    {
        ObjectLocator[] approved = [.. guard.Preconditions.Where(p => p.Tag == "objectHash" && p.Locator?.Kind == "function")
            .Select(p => p.Locator!)];
        string[] names = [.. approved.Select(l => l.Name).OfType<string>().Distinct(StringComparer.Ordinal)];
        // Use the approved resolved full tag; never infer a designation from a shown/short tag.
        if (names.Length != 1 || names[0] != deviceName || approved.Length is < 1 or > 200)
        {
            return false;
        }

        var expected = new HashSet<(string Kind, int Type, long Oid, string? Name, string? Page)>(
            approved.Select(l => (l.Kind, l.Type, l.Oid, l.Name, l.Page)));
        var actual = new HashSet<(string Kind, int Type, long Oid, string? Name, string? Page)>(
            functions.Where(f => string.Equals(f.Name, deviceName, StringComparison.Ordinal))
                .Select(f => (f.Kind, f.Type, f.Oid, (string?)f.Name, f.Page)));
        // Main is deliberately absent from locators; its change is covered by the standard objectHash checks.
        return expected.Count == approved.Length && expected.SetEquals(actual);
    }
}
