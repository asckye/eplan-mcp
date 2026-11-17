namespace EplanMcp.Contracts;
/// <summary>At most 200 borrowed element handles; native collections also expose their own handle.</summary>
public sealed class ApiHandleElements
{
    /// <summary>Independent handles for the first 200 elements, omitting null elements.</summary>
    public ApiObjectHandle[] Items { get; set; } = [];
    /// <summary>A native collection receiver when available; arrays have no collection receiver.</summary>
    public ApiObjectHandle? Collection { get; set; }
}
