using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Packs;

/// <summary>
/// The steps of importing reference and basic projects that need EPLAN (M6 design 3.2 step 1, 4.4, 4.6; ADR-0024
/// section 5): restore a <c>.zw1</c> into a new empty folder in scratch (never over a project: EPLAN overwrites it,
/// A31), tell whether a project opens in this EPLAN without opening it (<c>GetProjectDataCompatibility</c>, A31), and
/// extract its snapshot and facts on the scratch copy. They are the Bridge operations <c>project.restore</c>,
/// <c>project.compatibility</c> and <c>project.snapshot</c>, which another change builds. Until then the import
/// records each such check as a gap document, which these steps are to close.
/// </summary>
internal interface IReferenceProjectSteps
{
    /// <summary>Runs the steps for the projects <paramref name="change"/> added or replaced; what they found, for the result.</summary>
    public Task<IReadOnlyList<ScanFinding>> RunAsync(DevPackChange change, CancellationToken cancellationToken);
}

/// <summary>
/// The steps while the Bridge has no operations for them: none run, and the import's gap documents say what to check.
/// TODO(M6): replace with steps over project.restore, project.compatibility and project.snapshot once the Bridge has
/// them, writing facts as FactSet documents and closing the gaps they settle.
/// </summary>
internal sealed class GapOnlyReferenceProjectSteps : IReferenceProjectSteps
{
    public Task<IReadOnlyList<ScanFinding>> RunAsync(DevPackChange change, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScanFinding>>([]);
}
