using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// A fake EPLAN connection adapter for the functions of <see cref="SampleDevices"/>, shaped like spike S6's sample
/// project (api-notes A11): cross-sections in regional format, colour codes, a connection at an auxiliary function on
/// another page, an end without names, and a 3D connection. Narrows like the adapter: by device, connections at the
/// tag or at the tag with a ':' suffix; by page, connections placed on it.
/// </summary>
internal sealed class SampleConnections : IConnectionReader
{
    public List<string> Pages { get; } = ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2", "=GAB1+A2&EFS1/1", "=EC1+A3&EFS1/1"];

    public List<ConnectionRecord> Connections { get; } =
    [
        new() { StartDevice = "=GAA+A1-FC1", StartPin = "2", EndDevice = "=GAA+A1-MA1", EndPin = "U1", Page = "=GAA+A1&EFS1/1", CrossSection = "1,5", Colour = "GY" },
        new() { StartDevice = "=GAA+A1-FC1", StartPin = "4", EndDevice = "=GAA+A1-MA1", EndPin = "V1", Page = "=GAA+A1&EFS1/1", CrossSection = "1,5", Colour = "GY" },
        new() { StartDevice = "=GAA+A1-MA2", StartPin = "PE", EndDevice = "=GAA+A1-PF1:1", EndPin = "1", Page = "=GAA+A1&EFS1/2", CrossSection = "2,5", Colour = "GNYE" },
        new() { StartDevice = "=GAA+A1-FC1", StartPin = "13", EndDevice = "=GAA+A1-MA2", EndPin = "1", Page = "=GAA+A1&EFS1/2", CrossSection = "0,75" },
        new() { StartDevice = "=GAB1+A1-FC1", StartPin = "2", EndDevice = string.Empty, EndPin = string.Empty, Page = "=GAB1+A2&EFS1/1" },

        // The adapter reads no ends of a 3D connection; these only place it at -MA2 for the device narrowing.
        new() { Is3D = true, StartDevice = "=GAA+A1-MA2", EndDevice = "=GAA+A1-MA1", CrossSection = "1,5", Colour = "BU" },
    ];

    public IReadOnlyList<ConnectionRecord>? ReadConnections(string projectPath, string? device, string? page, bool detailed = false) =>
        page is not null && !Pages.Contains(page, StringComparer.Ordinal)
            ? null
            : [.. Connections.Where(c => (device is null || At(c.StartDevice, device) || At(c.EndDevice, device)) && (page is null || c.Page == page))];

    private static bool At(string? function, string device) =>
        function is not null && (function == device || function.StartsWith(device + ":", StringComparison.Ordinal));
}
