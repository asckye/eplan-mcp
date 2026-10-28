using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

internal sealed class ConnectionCreateFixture : ISymbolReader
{
    internal const string PageName = "=F+A1&EFS1/1";
    internal ConnectionCreatePlanner.Scene Scene { get; } = new()
    {
        Complete = true,
        Project = new() { Name = "Line1", Path = "scratch-project" },
        Page = new() { Project = "scratch-project", Page = PageName, GridSize = 4, PageSize = [100, 100] },
        SymbolBounds = new() { ["CO/0"] = [-1, -1, 1, 1], ["TLRO/8"] = [-1, -1, 1, 1], ["PCP/2"] = [-1, -1, 1, 1] },
    };
    internal List<SymbolSearchItem> Symbols { get; } =
    [
        Variant("CO", "Routing", 0, "Left", "Up"),
        Variant("TLRO", "TNodeUp", 8, "Left", "Right", "Up"),
        Variant("PCP", "PotentialTerminal", 2, "Up"),
    ];
    internal SymbolSearchArgs? Search { get; private set; }
    internal string? ProjectPath { get; private set; }
    internal int Reads { get; private set; }
    internal JObject Request { get; }

    internal ConnectionCreateFixture()
    {
        JObject from = AddPin(1, [8, 8], "Right", [4, 6, 8, 10]);
        JObject to = AddPin(2, [24, 24], "Down", [22, 24, 26, 28]);
        Request = new() { ["project"] = "Line1", ["page"] = PageName, ["dry_run"] = true, ["items"] = new JArray(Wire(from, to, [8, 8], [24, 8], [24, 24])) };
    }

    internal JObject AddPin(long oid, double[] location, string direction, double[] bounds)
    {
        var locator = new DrawingLocator { Project = "scratch-project", Page = PageName, Type = 17, Oid = oid, Digest = new string('a', 64) };
        Scene.Page.Placements.Add(new() { Locator = locator, Kind = "Function", Location = location, Bounds = bounds });
        Scene.Pins.Add(FormattableString.Invariant($"17/{oid}/0"), new() { Location = location, Direction = direction });
        return new() { ["locator"] = BridgeJson.FromObject(locator), ["pin_index"] = 0 };
    }

    internal JObject Plan(string? fingerprint = null) => ConnectionCreatePlanner.Plan(Request.ToString(), Scene, this, fingerprint);
    public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args)
    {
        ProjectPath = projectPath;
        Search = args;
        Reads++;
        return Symbols;
    }

    internal static JObject Wire(JObject from, JObject to, params double[][] points) => new()
    {
        ["kind"] = "wire",
        ["from"] = from,
        ["to"] = to,
        ["points"] = JArray.FromObject(points),
    };

    internal static JObject Node(string kind, double[] position, params string[] directions) => new()
    {
        ["kind"] = kind,
        ["position"] = new JArray(position),
        ["directions"] = new JArray(directions),
    };

    internal static SymbolSearchItem Variant(string name, string type, int variant, params string[] directions) => new()
    {
        Library = "SPECIAL",
        Name = name,
        Variant = variant,
        Type = type,
        Kind = name switch { "CO" => "routing", "PCP" => "potential_point", _ => "t_node" },
        ConnectionPoints = directions.Select(d => new SymbolConnectionPoint { Location = [0, 0], Direction = d }).ToList(),
    };
}
