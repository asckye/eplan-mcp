using System.Text.Json;
using EplanMcp.Contracts;
namespace EplanMcp.Large.Tests;

/// <summary>Read-only discovery and get for catalog-derived kinds; never creates fixture objects.</summary>
[Trait("Size", "Large")]
public sealed class GenericObjectReadTests(LiveEplan eplan, WriteHost host, ITestOutputHelper output) : IClassFixture<WriteHost>
{
    private static readonly HashSet<string> _legacy = new(StringComparer.Ordinal)
    {
        "Page", "Function", "Connection", "Terminal", "PLCTerminal", "TerminalStrip", "PLC", "Cable", "BlackBox",
        "PlaceHolder", "InterruptionPoint", "PotentialDefinition", "LocationBox", "MacroBox", "BoxedDevice",
    };

    [Fact]
    public async Task AtLeastTenNewKinds_FindAndGetInLargeWrite_WithoutDesignChanges()
    {
        var readKinds = new List<string>();
        var skipped = new List<string>();
        Dictionary<string, string> before = LiveSnapshots.DesignFiles(host.Writable);
        try
        {
            foreach (string kind in ObjectReadTypes.Types.Where(ObjectReadTypes.Supported).Where(k => !_legacy.Contains(k)))
            {
                JsonElement found = await eplan.CallAsync("eplan_object_find", new() { ["project"] = host.Writable, ["object_type"] = kind, ["limit"] = 1 });
                Assert.Equal("supported", found.GetProperty("status").GetString());
                if (found.GetProperty("items").GetArrayLength() == 0)
                { skipped.Add(kind); output.WriteLine("Skipped (zero objects): " + kind); continue; }
                JsonElement obj = Assert.Single(found.GetProperty("items").EnumerateArray());
                JsonElement read = await eplan.CallAsync("eplan_object_get", new()
                {
                    ["project"] = host.Writable,
                    ["object_type"] = kind,
                    ["locator"] = obj.GetProperty("locator"),
                    ["language"] = "en_US",
                    ["limit"] = 200,
                });
                Assert.Equal("supported", read.GetProperty("status").GetString());
                Assert.NotEmpty(read.GetProperty("items").EnumerateArray());
                Assert.All(read.GetProperty("items").EnumerateArray(), r => Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("property_name").GetString())));
                readKinds.Add(kind);
                output.WriteLine("Found and read: " + kind);
            }
        }
        finally
        {
            output.WriteLine("Read kinds: " + string.Join(", ", readKinds));
            output.WriteLine("Skipped zero-object kinds: " + string.Join(", ", skipped));
            Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(host.Writable).OrderBy(p => p.Key, StringComparer.Ordinal));
        }
        Assert.True(readKinds.Count >= 10, "LargeWrite must contain at least ten newly supported runtime kinds; found " + readKinds.Count);
    }
}
