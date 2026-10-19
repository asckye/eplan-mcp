namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The two names of a main function, as <see cref="IDeviceReader.ReadMainFunctionNames"/> reads them for a tag lookup.</summary>
public sealed class FunctionName
{
    /// <summary><c>Function.Name</c>, the full tag; empty for a function without a name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>Function.VisibleName</c> as EPLAN returns it: may be empty or carry '&gt;' markers (api-notes A11).</summary>
    public string VisibleName { get; set; } = string.Empty;
}
