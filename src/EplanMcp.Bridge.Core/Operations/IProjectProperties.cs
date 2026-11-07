namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and sets a project's title block properties for eplan_project_update (ADR-0052), implemented by the EPLAN
/// adapter layer. Main thread only; <see cref="Write"/> only inside the write's transaction.
/// </summary>
public interface IProjectProperties
{
    /// <summary>The stored text of a property, given as its number or identifying name; empty when not set; a multi-language property as <c>zh_CN@text;en_US@text;</c>.</summary>
    public string Read(string projectPath, string key);

    /// <summary>Sets the property; EPLAN refusing it is EPLAN_OPERATION_FAILED.</summary>
    public void Write(string projectPath, string key, string value);
}
