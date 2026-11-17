using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads one explicit open project's page on the main thread inside LockingStep; owns no returned EPLAN objects.</summary>
public interface IPageDrawingReader
{
    /// <summary>Detached geometry, or null when the exact page does not exist; no navigation, transactions or writes.</summary>
    public PageDrawingGeometry? Read(string projectPath, string pageName);
}
