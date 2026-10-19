using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

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

    public IReadOnlyList<FunctionName> ReadMainFunctionNames(string projectPath) =>
        [.. Functions.Where(f => f.IsMain).Select(f => new FunctionName { Name = f.Name, VisibleName = f.VisibleName })];

    /// <summary>The main function with this full tag; its auxiliary functions are the others whose tag starts with it.</summary>
    public DeviceRecord? ReadDevice(string projectPath, string tag, string? language, bool withProperties)
    {
        FunctionRecord? main = Functions.FirstOrDefault(f => f.IsMain && f.Name == tag);
        return main is null
            ? null
            : new DeviceRecord
            {
                Main = new FunctionDetail { Name = main.Name, VisibleName = main.VisibleName, Category = main.Category, Page = main.Page, Text = "功能文本", Pins = ["1", string.Empty, "2"] },
                Auxiliary =
                [
                    .. Functions
                        .Where(f => !f.IsMain && f.Name.StartsWith(main.Name, StringComparison.Ordinal))
                        .Select(f => new FunctionDetail { Name = f.Name, VisibleName = f.VisibleName, Category = f.Category, Page = f.Page }),
                ],
                Parts = [.. Enumerable.Range(1, main.Parts).Select(i => new DevicePart { PartNumber = $"PXC.{i}", Variant = "1", Count = 1 })],
                Properties = withProperties ? [new PropertyEntry { Id = 20011, Name = "Function text", Value = language ?? "default" }] : null,
            };
    }
}
