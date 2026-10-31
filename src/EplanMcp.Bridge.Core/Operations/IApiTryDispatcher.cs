using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Main-thread adapter; only generated compiled calls may implement invocation.</summary>
public interface IApiTryDispatcher
{
    /// <summary>Rejects a stale discovery locator without calling the trial member.</summary>
    public void CheckTarget(ApiTryArgs args);
    /// <summary>Reads bounded business and design-file observations.</summary>
    public Dictionary<string, string> Inspect(string project);
    /// <summary>Invokes one generated scalar read and returns its detached result.</summary>
    public string? Invoke(ApiTryArgs args);
}
