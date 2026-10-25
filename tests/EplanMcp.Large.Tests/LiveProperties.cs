using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>Read-only invoke helpers; no property MCP tool or native object is needed by these tests.</summary>
internal static class LiveProperties
{
    internal const string PowerSupplyPage = "=GAA+A1&EFS1/1";

    internal static PropertyReadArgs Args(LiveEplan eplan, bool project, params int[] ids) => new()
    {
        ProjectPath = eplan.ProjectPath,
        Target = project ? new PropertyTarget(projectObject: true) : new PropertyTarget(page: PowerSupplyPage),
        Properties = [.. ids.Select(id => new PropertySelector(propertyId: id))],
        Language = "en_US",
        Detailed = true,
    };

    internal static async Task<List<PropertyReadRow>> ReadAsync(BridgeClient bridge, PropertyReadArgs args)
    {
        var rows = new List<PropertyReadRow>();
        var positions = new HashSet<(int Selector, int Index)>();
        args.SelectorOffset = 0;
        args.IndexOffset = 0;
        do
        {
            Assert.True(positions.Add((args.SelectorOffset, args.IndexOffset)), "Property continuation repeated a position.");
            Assert.InRange(positions.Count, 1, 100);
            PropertyReadData block = await bridge.InvokeAsync<PropertyReadArgs, PropertyReadData>(OperationNames.PropertyRead,
                args, TimeSpan.FromSeconds(30), null, TestContext.Current.CancellationToken);
            Assert.InRange(block.Items.Count, 0, 200);
            rows.AddRange(block.Items);
            if (block.NextSelector is not { } next)
            {
                return rows;
            }

            args.SelectorOffset = next;
            args.IndexOffset = block.NextIndexOffset;
        }
        while (true);
    }

    internal static async Task DescribeEnvironmentAsync(BridgeClient bridge, int processId)
    {
        HandshakeResult handshake = await bridge.SelectAsync(processId, TestContext.Current.CancellationToken);
        Assert.Contains(OperationNames.PropertyRead, handshake.Operations!);
        TestContext.Current.TestOutputHelper?.WriteLine($"UTC {DateTimeOffset.UtcNow:O}; EPLAN {handshake.Eplan.Version}; "
            + $"variant {handshake.Eplan.Variant}; API extension {handshake.Eplan.ApiExtensionLicence}; offline {handshake.Eplan.Offline}");
    }

    internal static void Describe(IEnumerable<PropertyReadRow> rows)
    {
        foreach (PropertyReadRow row in rows)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"ID {row.PropertyId}, index {row.Index}, status {row.Status}, "
                + $"language {row.Language}, fallback {row.Fallback}, definition {JsonSerializer.Serialize(row.Definition, HostJson.Options)}");
        }
    }
}
