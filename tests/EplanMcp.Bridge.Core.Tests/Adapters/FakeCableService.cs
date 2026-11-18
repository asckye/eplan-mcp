using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;

namespace Eplan.EplApi.HEServices;

internal sealed class CableService : IDisposable
{
    private bool _disposed;
    // UNVERIFIED (2026-11-18 survey E4-23): Reassignment checks only the count and leaves allocation unchanged.
    public void DoReassignWires(Cable cable, Connection[] templates, bool assign, Connection[] excluded)
    { ObjectDisposedException.ThrowIf(_disposed, this); Assert.Equal(cable.CableConnections.Length, templates.Length); }
    public void Dispose() => _disposed = true;
}
