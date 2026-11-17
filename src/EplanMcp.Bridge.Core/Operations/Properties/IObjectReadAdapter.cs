using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;

public interface IObjectReadAdapter : IPropertyReadAdapter
{
    public IReadOnlyList<ObjectSummary> Find(ObjectReadArgs args);
}
