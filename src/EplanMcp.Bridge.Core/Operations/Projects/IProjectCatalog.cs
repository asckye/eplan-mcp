using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read access to EPLAN's open projects, implemented by the EPLAN adapter layer. Called on the main thread only.</summary>
public interface IProjectCatalog
{
    /// <summary>All open projects in EPLAN's order, marking the currently selected one. Must not lock any project.</summary>
    public IReadOnlyList<OpenProject> ListOpenProjects();
}
