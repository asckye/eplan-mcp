using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

internal sealed class CableDefinitionFixture
{
    internal ConnectionCreateFixture Base { get; } = new();
    internal CableDefinitionPlanner.Inventory Inventory { get; } = new() { Complete = true, Names = ["-W99"] };
    internal JObject Cable => (JObject)Base.Request["items"]![1]!;
    internal JArray Cores => (JArray)Cable["cores"]!;
    internal CableDefinitionFixture()
    {
        Base.Scene.ReadCables = () => Inventory;
        ((JArray)Base.Request["items"]!).Add(Definition("-W3", [[16, 4], [16, 12]], Core(new JObject { ["item_index"] = 0 }, "1", "BK")));
    }
    internal CableDefinitionPlanner.Wire AddExisting()
    {
        Base.AddPin(3, [40, 8], "Up", [38, 4, 42, 8]);
        Base.AddPin(4, [40, 40], "Down", [38, 40, 42, 44]);
        var wire = new CableDefinitionPlanner.Wire
        {
            Locator = new DrawingLocator { Project = "scratch-project", Page = ConnectionCreateFixture.PageName, Type = 9, Oid = 9, Digest = new string('c', 64) },
            Points = [[40, 8], [40, 40]],
            Pins = ["17/3/0", "17/4/0"],
            Label = "7",
            Colour = "BU",
            CrossSection = "1,5",
        };
        Inventory.Wires.Add(wire);
        Base.Scene.Graphical.Add(wire.Points);
        Base.Scene.Logical.Add(new() { StartDevice = "17/3/0", EndDevice = "17/4/0" });
        return wire;
    }
    internal JObject Plan(string? expected = null) => Base.Plan(expected);
    internal static JObject Definition(string designation, double[][] points, params JObject[] cores) => new()
    {
        ["kind"] = "cable_definition",
        ["points"] = JArray.FromObject(points),
        ["designation"] = designation,
        ["type_text"] = "ÖLFLEX CLASSIC 100 300/500V",
        ["core_count"] = 3,
        ["cross_section_mm2"] = 1.5,
        ["cores"] = new JArray(cores),
    };
    internal static JObject Core(JObject connection, string label, string colour) => new() { ["connection"] = connection, ["label"] = label, ["colour"] = colour };
    internal static JObject Reference(CableDefinitionPlanner.Wire wire) => new() { ["locator"] = BridgeJson.FromObject(wire.Locator) };
}
