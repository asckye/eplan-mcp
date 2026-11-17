using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Checks native types before returning a transaction-only removal action.</summary>
public interface IPlacementRemover
{
    public Action Prepare(string project, string page, IReadOnlyList<DrawingLocator> items);
}
