using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ObjectResolve"/> (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.6): every locator is
/// found by its number and checked by <see cref="LocatorCheck"/>; a resolved one carries the content hash a snapshot
/// would give its object, so the Host can check a plan's preconditions. Reads only, open projects only.
/// </summary>
public sealed class ObjectResolveOperation(IProjectCatalog catalog, IObjectFinder finder) : Operation<ObjectResolveArgs, ObjectResolveData>
{
    public const int MaxLocators = 200;

    public override string Name => OperationNames.ObjectResolve;

    protected override ObjectResolveData Execute(ObjectResolveArgs args)
    {
        List<ObjectLocator> locators = args.Locators ?? [];
        var violations = new List<string>();
        if (locators.Count is 0 or > MaxLocators)
        {
            violations.Add($"'locators' must hold 1 to {MaxLocators} locators.");
        }

        for (int i = 0; i < Math.Min(locators.Count, MaxLocators); i++)
        {
            violations.AddRange(Violations(locators[i], i));
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), Given(args.Project) ?? locators[0].Project);
        string profile = SnapshotProfiles.StandardName;
        IReadOnlyDictionary<int, SnapshotProperty> registered = SnapshotCanonical.Registered(profile)!;
        IReadOnlyList<SnapshotSourceObject?> found = finder.Find(project.Path, locators, profile);
        return new ObjectResolveData
        {
            Project = project.Name,
            ProjectPath = project.Path,
            Results = [.. locators.Select((locator, i) => LocatorCheck.Check(i, locator, project.Path, found[i] is { } o ? SnapshotCanonical.ObjectOf(o, registered) : null))],
        };
    }

    private static IEnumerable<string> Violations(ObjectLocator? locator, int index)
    {
        string at = "locators[" + index.ToString(CultureInfo.InvariantCulture) + "]";
        if (locator is null)
        {
            yield return at + " must be a locator.";
            yield break;
        }

        if (string.IsNullOrWhiteSpace(locator.Project))
        {
            yield return at + ".project is required: the project path the snapshot gave.";
        }

        if (!LocatorCheck.Kinds.Contains(locator.Kind))
        {
            yield return at + ".kind must be page, function or connection.";
        }

        if (locator.Type <= 0 || locator.Oid <= 0)
        {
            yield return at + ".type and .oid must be the positive numbers the snapshot gave.";
        }

        if (locator.Pos is not null && LocatorCheck.Position(locator.Pos) is null)
        {
            yield return at + ".pos must be 'x,y' in millimetres, as the snapshot gave it.";
        }
    }
}
