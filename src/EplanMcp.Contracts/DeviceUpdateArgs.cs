namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.DeviceUpdatePlan"/> and <see cref="OperationNames.DeviceUpdateExecute"/>.</summary>
public sealed class DeviceUpdateArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The device's tag, full (<c>=GAA+A1-KF1</c>) or as shown (<c>-KF1</c>), found as eplan_device_get finds it.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>The new tag: full, or starting with <c>-</c> to keep the device's structure.</summary>
    public string NewTag { get; set; } = string.Empty;

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}
