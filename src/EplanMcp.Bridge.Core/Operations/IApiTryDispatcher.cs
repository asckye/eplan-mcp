using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Main-thread adapter; only generated compiled calls may implement invocation.</summary>
public interface IApiTryDispatcher
{
    /// <summary>Rejects a stale discovery locator without calling the trial member.</summary>
    public void CheckTarget(ApiTryArgs args);
    /// <summary>Reads bounded business and design-file observations.</summary>
    public Dictionary<string, string> Inspect(string project);
    /// <summary>Reads the generated property before a write.</summary>
    public string? Read(ApiTryArgs args);
    /// <summary>Invokes one generated member, returning text, a detached locator or null.</summary>
    public object? Invoke(ApiTryArgs args);
}
