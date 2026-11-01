using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Fresh, detached lifecycle observations; no transaction-owned native handles are retained.</summary>
public interface IApiTryLifecycleDispatcher
{
    /// <summary>Re-enumerates affected-page identities (or unpaged project objects) before and after creation or removal.</summary>
    public Dictionary<string, string> Observe(ApiTryArgs args);
    /// <summary>Refreshes a created locator after commit, using only its detached identity.</summary>
    public DrawingLocator Refresh(string project, DrawingLocator locator);
}
