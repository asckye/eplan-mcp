using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Main-thread work unit: fresh inventory, one list per object through re-plan/write/readback; Dispose is idempotent (A36).</summary>
public interface IPropertySetSession : IPropertyReadAdapter, IDisposable
{
    public IReadOnlyList<SnapshotObject> Functions { get; }
    public IReadOnlyList<string> Languages { get; }
    public void Write(PropertySetGuard guard);
}
