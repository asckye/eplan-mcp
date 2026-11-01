using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Main-thread adapter; only generated compiled calls may implement invocation.</summary>
public interface IApiTryDispatcher
{
    /// <summary>Rejects a stale discovery locator without calling the trial member.</summary>
    public void CheckTarget(ApiTryArgs args);
    /// <summary>Reads bounded observations of affected pages, or project-wide evidence when no array page scope is available.</summary>
    public Dictionary<string, string> Inspect(ApiTryArgs args);
    /// <summary>Reads the generated property before a write.</summary>
    public string? Read(ApiTryArgs args);
    /// <summary>Invokes one generated member, returning text, a detached locator, session handle, shaped JSON or null.</summary>
    public object? Invoke(ApiTryArgs args);
}
