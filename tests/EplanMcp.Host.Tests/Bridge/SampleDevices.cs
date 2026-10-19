using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// A fake EPLAN device adapter with functions shaped like spike S6's sample project (api-notes A11): the same short
/// tag -FC1 in two structures, '&gt;' markers, empty shown tags, functions without a name, auxiliary functions.
/// </summary>
internal sealed class SampleDevices : IDeviceReader
{
    public List<FunctionRecord> Functions { get; } =
    [
        new() { Name = "=GAA+A1-FC1", VisibleName = "-FC1", IsMain = true, Category = "CircuitBreaker", Page = "=GAA+A1&EFS1/1", Parts = 2 },
        new() { Name = "=GAA+A1-FC1", VisibleName = "-FC1", IsMain = false, Category = "NoContact", Page = "=GAA+A1&EFS1/2" },
        new() { Name = string.Empty, VisibleName = string.Empty, IsMain = true, Category = "Blackbox", Page = "=GAA+A1&EFS1/1" },
        new() { Name = "=GAB1+A1-FC1", VisibleName = "-FC1", IsMain = true, Category = "MotorOverloadSwitch", Page = "=GAB1+A2&EFS1/1", Parts = 1 },
        new() { Name = "+A1-FC2", VisibleName = "+A1-FC2", IsMain = true, Category = "TerminalDefText", Parts = 1 },
        new() { Name = "=+-CU1", VisibleName = ">=>+>-CU1", IsMain = true, Category = "Harness", Page = "=EC1+A3&EFS1/1" },
        new() { Name = "=GAA+A1-PF1:1", VisibleName = string.Empty, IsMain = false, Category = "DeviceEndTerminal", Page = "=GAA+A1&EFS1/1" },
        new() { Name = "=GAA+A1-MA1", VisibleName = "-MA1", IsMain = true, Category = "Motor", Page = "=GAA+A1&EFS1/2" },
        new() { Name = "=GAA+A1-MA2", VisibleName = "-MA2", IsMain = true, Category = "Motor", Page = "=GAA+A1&EFS1/2", Parts = 1 },
    ];

    public bool IsCategory(string category) =>
        new[] { "CircuitBreaker", "NoContact", "Blackbox", "MotorOverloadSwitch", "TerminalDefText", "Harness", "DeviceEndTerminal", "Motor", "Coil" }
            .Contains(category, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FunctionRecord>? ReadFunctions(string projectPath, string? page, string? category) =>
        page is not null && !Functions.Any(f => f.Page == page)
            ? null
            : [.. Functions.Where(f => (page is null || f.Page == page) && (category is null || string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase)))];
}
