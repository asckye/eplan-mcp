namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Editor navigation on the main thread inside LockingStep, without a write transaction (A54).</summary>
public interface IPageEditor
{
    /// <summary>Opens or focuses an exact page in an open project; returns its name, or null when absent.</summary>
    public string? Open(string projectPath, string pageName);
}
