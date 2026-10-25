using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Main-thread only. Open exactly one property list; failed acquisition must dispose partial resources (A36).</summary>
public interface IPropertyReadAdapter
{
    public IPropertyReadScope Open(string projectPath, PropertyTarget target);
}
