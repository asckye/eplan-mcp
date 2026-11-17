using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Main-thread UI access inside LockingStep; no project transaction or retained native objects.</summary>
public interface ISelectionAdapter
{
    public SelectionData Read();
    public void Validate(string project, IReadOnlyList<DrawingLocator> locators);
    public void Apply(string? project, IReadOnlyList<DrawingLocator> locators, bool showPage);
    /// <summary>Activate the GED before reading its selection; false means no editor could take focus.</summary>
    public bool FocusEditor();
}
