namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.DeviceGet"/> (docs/tools/eplan_device_get.md).</summary>
public sealed class DeviceGetArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; null means the project selected in EPLAN.</summary>
    public string? Project { get; set; }

    /// <summary>The full tag (<c>=GAA+A1-FC1</c>), or the tag shown on the page (<c>-FC1</c>) when it is unique.</summary>
    public string? Tag { get; set; }

    /// <summary>Language code for multi-language texts, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }

    /// <summary>Whether to return every non-empty, non-indexed property of the main function as well.</summary>
    public bool Detailed { get; set; }
}
