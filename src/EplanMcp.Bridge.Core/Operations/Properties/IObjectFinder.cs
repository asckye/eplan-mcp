using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Finds objects by kind and <c>ObjectIdentifier</c> for object.resolve, implemented by the EPLAN adapter layer
/// (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.6). Main thread only, inside the dispatcher's LockingStep.
/// </summary>
public interface IObjectFinder
{
    /// <summary>
    /// For each locator, in order, the object of its kind with its <c>ObjectIdentifier</c> in the open project at
    /// <paramref name="projectPath"/>, read exactly as the snapshot reads it with the registered properties of
    /// <paramref name="profile"/>, or null when there is none. Where several objects of the kind share the number, the
    /// one of the locator's type number. A function is looked for on the locator's page first, then on every page.
    /// </summary>
    public IReadOnlyList<SnapshotSourceObject?> Find(string projectPath, IReadOnlyList<ObjectLocator> locators, string profile);
}
