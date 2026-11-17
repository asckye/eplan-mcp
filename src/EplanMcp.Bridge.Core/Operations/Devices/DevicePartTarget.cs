using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A part target's managed identity and existing part references; never retains native wrappers.</summary>
public sealed class DevicePartTarget
{
    public bool IsMain { get; set; } = true;
    public string Category { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string? VisibleName { get; set; }
    public string? Page { get; set; }
    public List<DevicePart> Parts { get; set; } = [];
}
