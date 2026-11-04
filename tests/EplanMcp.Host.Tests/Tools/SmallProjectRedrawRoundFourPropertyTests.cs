using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawRoundFourPropertyTests
{
    [Fact]
    public async Task RepeatedDisplayedProperty_MapsNativeIndicesByContentToCollectionSlots()
    {
        // Round-three input: property 20028 indices 1..6 share an OID and alternating locations.
        JObject owner = Owner(27224, "Function", "=GAA+A1-FC1", 96, 260);
        owner["symbol"] = new JObject { ["library"] = "IEC_symbol", ["name"] = "Q_3LI", ["variant"] = 6 };
        var source = new List<JObject> { owner };
        JObject targetOwner = Drawing(owner, 182);
        var targets = new List<JObject> { targetOwner };
        for (int index = 1; index <= 6; index++)
        {
            double y = index % 2 == 1 ? 0 : -0.5;
            JObject property = Property(5241, 27224, 20028, index, -0.5, y, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            property["angle"] = 1.5707963267948997;
            property["layer"] = 420;
            source.Add(property);
            JObject target = PropertyDrawing(targetOwner, 20028, 6 + 2 * index, 95.5, 260 + y, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            target["properties"]!["angle_deg"] = "90";
            target["properties"]!["layer"] = "419";
            targets.Add(target);
        }
        var rig = new Rig(targets);

        await rig.Draw(source);

        JObject[] moves = rig.Moves();
        Assert.Equal(6, moves.Length);
        Assert.Equal([8, 10, 12, 14, 16, 18], moves.Select(m => (int)m["property_index"]!).ToArray());
        Assert.All(moves, move => Assert.Equal(20028, (int)move["property_id"]!));
        Assert.Empty((JArray)rig.Report.ToJson()["warnings"]!);
    }

    [Fact]
    public async Task ParentMatching_UsesExactTerminalNameDespiteDeviceTagAlias()
    {
        JObject owner = Owner(1, "Terminal", "-PLC1:1.2", 238, 272);
        owner["category"] = "PLCTerminal";
        owner["parentFunction"] = new JObject { ["name"] = "-PLC1" };
        owner["pins"] = new JArray(new JObject { ["name"] = "1.2", ["index"] = 0 });
        JObject target = Drawing(owner, 101);
        var rig = new Rig([target, PropertyDrawing(target, 20038, 0, 240, 272, "1.2")], "-PLC1");
        JObject property = Property(2, 1, 20038, 0, 2, 0, "1.2");
        property["layer"] = 428;

        await rig.Draw([owner, property]);

        Assert.Equal(101, (int)Assert.Single(rig.Moves())["parent"]!["oid"]!);
    }

    [Fact]
    public async Task ParentMatching_UsesPageAndPositionForDuplicateRepresentations()
    {
        JObject owner = Owner(1, "Function", "-PLC1:1.1", 228, 92);
        JObject correct = Drawing(owner, 101);
        JObject overview = Drawing(Owner(3, "Function", "-PLC1:1.1", 238, 272), 102);
        JObject otherPage = Drawing(owner, 103);
        otherPage["locator"]!["page"] = "/2";
        var rig = new Rig([overview, otherPage, correct,
            PropertyDrawing(overview, 20028, 0, 240, 272, "1.1"), PropertyDrawing(correct, 20028, 7, 0, 0, "1.1")]);

        await rig.Draw([owner, Property(2, 1, 20028, 1, 2, 0, "1.1")]);

        Assert.Equal(101, (int)Assert.Single(rig.Moves())["parent"]!["oid"]!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingSourceId_IsInferredFromOwnerValueOrUniquePosition(bool ownerValue)
    {
        JObject owner = Owner(1, "Function", "-K1", 10, 20);
        if (ownerValue)
        { owner["properties"] = new JArray(new JObject { ["id"] = 20028, ["index"] = 2, ["value"] = "2" }); }
        JObject target = Drawing(owner, 101);
        JObject property = Property(2, 1, 20028, 2, 2, 0, "2");
        property.Remove("propertyId");
        property["layer"] = 420;
        JObject targetProperty = PropertyDrawing(target, 20028, 10, ownerValue ? 0 : 12, ownerValue ? 0 : 20, "2");
        var rig = new Rig([target, targetProperty]);

        await rig.Draw([owner, property]);

        Assert.Equal(20028, (int)Assert.Single(rig.Moves())["property_id"]!);
        Assert.Contains(((JArray)rig.Report.ToJson()["notes"]!).OfType<JObject>(),
            note => ((string?)note["reason"])?.Contains("inferred", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task UnreadableTargetId_UsesContentAndPositionButKeepsExportedId()
    {
        JObject owner = Owner(1, "Function", "-K1", 10, 20);
        JObject target = Drawing(owner, 101);
        JObject targetProperty = PropertyDrawing(target, 20028, 10, 12, 20, "2");
        ((JObject)targetProperty["properties"]!).Remove("property_id");
        JObject property = Property(2, 1, 20028, 2, 2, 0, "2");
        property["layer"] = 420;
        var rig = new Rig([target, targetProperty]);

        await rig.Draw([owner, property]);

        JObject move = Assert.Single(rig.Moves());
        Assert.Equal(20028, (int)move["property_id"]!);
        Assert.Equal(10, (int)move["property_index"]!);
    }

    [Fact]
    public async Task MissingSourceId_UsesKnownNativeIndexWhenOwnerValuesRepeat()
    {
        JObject owner = Owner(1, "Function", "-K1", 10, 20);
        owner["properties"] = new JArray(
            new JObject { ["id"] = 20028, ["index"] = 2, ["value"] = "2" },
            new JObject { ["id"] = 20029, ["index"] = 3, ["value"] = "2" });
        JObject target = Drawing(owner, 101);
        JObject property = Property(2, 1, 20028, 2, 2, 0, "2");
        property.Remove("propertyId");
        property["layer"] = 420;
        var rig = new Rig([target, PropertyDrawing(target, 20028, 10, 0, 0, "2")]);

        await rig.Draw([owner, property]);

        Assert.Equal(20028, (int)Assert.Single(rig.Moves())["property_id"]!);
    }

    [Fact]
    public async Task ParentDigestChanges_DoNotLosePropertyOwner()
    {
        JObject owner = Owner(1, "Function", "-K1", 10, 20);
        JObject target = Drawing(owner, 101);
        target["locator"]!["digest"] = "latest-owner-digest";
        JObject targetProperty = PropertyDrawing(target, 20028, 10, 12, 20, "2");
        targetProperty["parent"]!["digest"] = "previous-owner-digest";
        JObject property = Property(2, 1, 20028, 2, 2, 0, "2");
        property["layer"] = 420;
        var rig = new Rig([target, targetProperty]);

        await rig.Draw([owner, property]);

        Assert.Equal(101, (int)Assert.Single(rig.Moves())["parent"]!["oid"]!);
    }

    [Fact]
    public async Task MissingDefaultPlacement_IsCountedSeparatelyWithNativePropertyId()
    {
        JObject owner = Owner(1, "Function", "-K1", 10, 20);
        var rig = new Rig([Drawing(owner, 101)]);

        await rig.Draw([owner, Property(2, 1, 20039, 0, 2, 0, "S1")]);

        Assert.Empty(rig.Moves());
        JObject report = rig.Report.ToJson();
        Assert.Equal(1, (int)report["missingPropertyPlacementCount"]!);
        JToken missing = Assert.Single((JArray)report["missingPropertyPlacements"]!);
        Assert.Equal(20039, (int)missing["propertyId"]!);
        Assert.Equal(1, (int)Assert.Single((JArray)report["missingPropertyIds"]!)["count"]!);
    }

    [Fact]
    public async Task AmbiguousOrUnreadableIdentity_IsReportedWithoutSelectingFirst()
    {
        JObject owner = Owner(1, "Function", "-K1", 10, 20);
        JObject target = Drawing(owner, 101);
        JObject property = Property(2, 1, 20028, 1, 2, 0, "");
        property.Remove("propertyId");
        var rig = new Rig([target, PropertyDrawing(target, 20028, 8, 12, 20, ""), PropertyDrawing(target, 20039, 9, 12, 20, "")]);

        await rig.Draw([owner, property]);

        Assert.Empty(rig.Moves());
        Assert.Contains(((JArray)rig.Report.ToJson()["warnings"]!).OfType<JObject>(),
            warning => ((string?)warning["reason"])?.Contains("cannot be inferred", StringComparison.Ordinal) == true);
        Assert.Equal(0, (int)rig.Report.ToJson()["missingPropertyPlacementCount"]!);
    }

    [Fact]
    public async Task DuplicateIndexedContents_RequireUnambiguousPosition()
    {
        JObject owner = Owner(1, "Function", "-K1", 10, 20);
        JObject target = Drawing(owner, 101);
        var rig = new Rig([target, PropertyDrawing(target, 20028, 8, 12, 20, "1"), PropertyDrawing(target, 20028, 10, 12, 20, "1")]);

        await rig.Draw([owner, Property(2, 1, 20028, 1, 2, 0, "1"), Property(2, 1, 20028, 2, 2, 0, "1")]);

        Assert.Empty(rig.Moves());
        Assert.Equal(2, ((JArray)rig.Report.ToJson()["warnings"]!).Count);
    }

    [Fact]
    public async Task RepeatedEmptyDescriptionsAtSamePosition_DoNotGuessNativeIndexFromCollectionOrder()
    {
        // Live round-three FC1 input: native indices 1/3/5 all have empty content at this position.
        JObject owner = Owner(27224, "Function", "=GAA+A1-FC1", 96, 260);
        owner["symbol"] = new JObject { ["library"] = "IEC_symbol", ["name"] = "Q_3LI", ["variant"] = 6 };
        JObject target = Drawing(owner, 182);
        var source = new List<JObject> { owner };
        var targets = new List<JObject> { target };
        for (int index = 1; index <= 5; index += 2)
        {
            JObject property = Property(5241, 27224, 20029, index, 0, 0.5, "");
            property["content"] = new JObject();
            property["angle"] = 1.5707963267948997;
            property["layer"] = 421;
            source.Add(property);
            targets.Add(PropertyDrawing(target, 20029, 7 + 2 * index, 96, 260.5, ""));
        }
        var rig = new Rig(targets);

        await rig.Draw(source);

        Assert.Empty(rig.Moves());
        JObject report = rig.Report.ToJson();
        Assert.Equal(0, (int)report["missingPropertyPlacementCount"]!);
        Assert.Equal(3, ((JArray)report["warnings"]!).Count);
        Assert.All(((JArray)report["warnings"]!).OfType<JObject>(),
            warning => Assert.Contains("collection slot is not the native property index", (string)warning["reason"]!, StringComparison.Ordinal));
    }

    private sealed class Rig(IEnumerable<JObject> targets, string? returnedTag = null)
    {
        internal List<(string Tool, JObject Args)> Calls { get; } = [];
        internal SmallProjectRedrawReport Report { get; } = new("synthetic", "ScratchTest");

        internal async Task Draw(IEnumerable<JObject> source)
        {
            JObject page = new() { ["name"] = "/1", ["pageType"] = "Circuit", ["placements"] = new JArray(source), ["connections"] = new JArray() };
            var driver = new SmallProjectRedrawDriver((tool, args) =>
            {
                Calls.Add((tool, (JObject)args.DeepClone()));
                return Task.FromResult(new JObject
                {
                    ["execution"] = "committed",
                    ["device"] = new JObject { ["tag"] = returnedTag ?? (string?)args["tag"] },
                    ["drawings"] = new JObject { ["items"] = new JArray(targets.Select(t => t.DeepClone())), ["truncated"] = false },
                });
            }, "ScratchTest", Path.GetTempPath(), Report);
            await driver.DrawPageAsync(page, [page]);
        }

        internal JObject[] Moves() => Calls.Where(c => c.Tool == "eplan_placement_update")
            .SelectMany(c => ((JArray)c.Args["property_moves"]!).OfType<JObject>()).ToArray();
    }

    private static JObject Owner(int oid, string type, string name, double x, double y) => new()
    {
        ["type"] = type,
        ["oid"] = oid,
        ["name"] = name,
        ["location"] = Point(x, y),
        ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = "K", ["variant"] = 0 },
        ["parts"] = new JArray(),
        ["pins"] = new JArray(),
    };

    private static JObject Drawing(JObject source, int oid) => new()
    {
        ["kind"] = source["type"]!.DeepClone(),
        ["locator"] = new JObject { ["type"] = source["type"]!.DeepClone(), ["oid"] = oid, ["page"] = "/1" },
        ["location"] = new JArray(source["location"]!["x"]!.DeepClone(), source["location"]!["y"]!.DeepClone()),
        ["properties"] = new JObject { ["name"] = source["name"]!.DeepClone() },
    };

    private static JObject Property(int oid, int parentOid, int propertyId, int index, double x, double y, string content) => new()
    {
        ["type"] = "ContextPropertyPlacement",
        ["oid"] = oid,
        ["parentOid"] = parentOid,
        ["propertyId"] = propertyId,
        ["propertyIndex"] = index,
        ["location"] = Point(x, y),
        ["angle"] = 0,
        ["content"] = new JObject { ["??_??"] = content },
    };

    private static JObject PropertyDrawing(JObject owner, int id, int slot, double x, double y, string content) => new()
    {
        ["kind"] = "PropertyPlacement",
        ["parent"] = owner["locator"]!.DeepClone(),
        ["location"] = new JArray(x, y),
        ["locator"] = new JObject { ["type"] = "PropertyPlacement", ["oid"] = 1000 + slot },
        ["properties"] = new JObject
        {
            ["property_id"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["property_index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["display"] = content,
            ["angle_deg"] = "0"
        },
    };

    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
}
