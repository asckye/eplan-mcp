using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class CableDefinitionPlannerTests
{
    [Theory]
    [InlineData("-W9", "+A1-W9", true)]
    [InlineData("=GAA+A1-W9", "+A1-W9", true)]
    [InlineData("=GAA+A1-W9", "=GAA+A1-W9", true)]
    [InlineData("-W9", "+A2-W9", false)]
    [InlineData("=GAA+A1-W9", "=OTHER+A1-W9", false)]
    [InlineData("-W9", "+A1-W90", false)]
    [InlineData("-W9", "+A1-W8", false)]
    public void CableDesignation_OnlyToleratesDroppedFunctionStructure(string requested, string actual, bool expected) =>
        Assert.Equal(expected, EplanMcp.Bridge.Core.Operations.CableDefinitionPlanner.SameName(requested, actual, "=GAA+A1&EFS1/1"));

    [Fact]
    public void SameCallWire_ForwardReference_PredictsExplicitCoreWithoutWriting()
    {
        var f = new CableDefinitionFixture();
        JToken wire = f.Base.Request["items"]![0]!;
        f.Cores[0]!["connection"]!["item_index"] = 1;
        f.Base.Request["items"] = new JArray(f.Cable, wire);
        JObject plan = f.Plan();
        Assert.Equal(ExecutionState.NotStarted, (string?)plan["execution"]);
        Assert.Null(plan["executionCapability"]);
        Assert.Equal(8, (int)plan["undoSteps"]!);
        Assert.Equal("1", (string?)Assert.Single((JArray)plan["coreImpact"]!)["label"]);
        Assert.Contains("bind -> generate -> cores", (string?)plan["generation"], StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeKeys_SameLabelOnDifferentCables_AcceptsAndDuplicateWithinCableRejects()
    {
        var f = new CableDefinitionFixture();
        var wire = f.AddExisting();
        ((JArray)f.Base.Request["items"]!).Add(CableDefinitionFixture.Definition("-W4", [[32, 16], [48, 16]], CableDefinitionFixture.Core(CableDefinitionFixture.Reference(wire), "1", "BU")));
        Assert.Equal(2, ((JArray)f.Plan()["coreImpact"]!).Count);
        f.Cores.Add(f.Cores[0]!.DeepClone());
        Invalid(f, "/items/1/cores/1/label");
    }

    [Theory]
    [InlineData("core_count", "0", "/core_count")]
    [InlineData("core_count", "51", "/core_count")]
    [InlineData("core_count", "3.0", "/core_count")]
    [InlineData("core_count", "\"3\"", "/core_count")]
    [InlineData("cross_section_mm2", "0", "/cross_section_mm2")]
    [InlineData("cross_section_mm2", "-1", "/cross_section_mm2")]
    [InlineData("cross_section_mm2", "\"1.5\"", "/cross_section_mm2")]
    [InlineData("cross_section_mm2", "1e400", "/cross_section_mm2")]
    [InlineData("cores", "[]", "/cores")]
    [InlineData("points", "[[16,4],[16,4]]", "/points")]
    [InlineData("points", "[[16,4],[20,12]]", "/points")]
    [InlineData("type_text", "null", "/type_text")]
    [InlineData("type_text", "\"bad\\ntext\"", "/type_text")]
    [InlineData("designation", "\"-W3/1\"", "/designation")]
    [InlineData("designation", "\"-\"", "/designation")]
    [InlineData("designation", "\"--W3\"", "/designation")]
    [InlineData("designation", "\"W3\"", "/designation")]
    [InlineData("unknown/member~", "1", "/unknown~1member~0")]
    [InlineData("", "1", "/")]
    public void Cable_StrictTypesFiniteNumbersAndMembers_RejectsWithPointer(string member, string value, string at)
    {
        var f = new CableDefinitionFixture();
        f.Cable[member] = JToken.Parse(value);
        Invalid(f, "/items/1" + at);
    }

    [Theory]
    [InlineData("label", "\"01\"")]
    [InlineData("label", "\"0\"")]
    [InlineData("label", "\"4\"")]
    [InlineData("label", "\"PE/1\"")]
    [InlineData("label", "\" PE\"")]
    [InlineData("label", "\"\\u202ePE\"")]
    [InlineData("label", "1")]
    [InlineData("colour", "\"bk\"")]
    [InlineData("colour", "\"\"")]
    [InlineData("colour", "31005")]
    [InlineData("connection", "{\"item_index\":-1}")]
    [InlineData("connection", "{\"item_index\":0.5}")]
    [InlineData("connection", "{\"item_index\":50}")]
    [InlineData("connection", "{\"item_index\":1}")]
    [InlineData("connection", "{\"item_index\":0,\"locator\":{}}")]
    [InlineData("connection", "null")]
    [InlineData("connection", "5")]
    [InlineData("connection", "[]")]
    public void Core_InvalidLabelColourOrReference_Rejects(string member, string value)
    {
        var f = new CableDefinitionFixture();
        f.Cores[0]![member] = JToken.Parse(value);
        Invalid(f, "/items/1/cores/0/" + member);
    }

    [Fact]
    public void Cores_DeclaredCountAndGlobalLimit_RejectsBeforeReadingInventory()
    {
        var f = new CableDefinitionFixture();
        f.Cable["core_count"] = 1;
        f.Cores.Add(CableDefinitionFixture.Core(new() { ["item_index"] = 0 }, "PE", "GNYE"));
        f.Base.Scene.ReadCables = () => throw new InvalidOperationException("Inventory must not be read.");
        Invalid(f, "/items/1/cores");
        f.Cable["core_count"] = 50;
        f.Cable["cores"] = new JArray(Enumerable.Range(1, 50).Select(n => CableDefinitionFixture.Core(new() { ["item_index"] = 0 }, n.ToString(System.Globalization.CultureInfo.InvariantCulture), "BK")));
        JObject second = (JObject)f.Cable.DeepClone();
        second["designation"] = "-W4";
        second["cores"] = new JArray(CableDefinitionFixture.Core(new() { ["item_index"] = 0 }, "1", "BK"));
        ((JArray)f.Base.Request["items"]!).Add(second);
        Invalid(f, "/items/2/cores/0");
    }

    [Fact]
    public void Assignment_DuplicateConnectionWithDifferentLabel_Rejects()
    {
        var f = new CableDefinitionFixture();
        f.Cores.Add(CableDefinitionFixture.Core(new() { ["item_index"] = 0 }, "PE", "GNYE"));
        Invalid(f, "/items");
    }

    [Fact]
    public void Cores_FiftyExplicitConnections_AcceptsUpperBound()
    {
        var f = new CableDefinitionFixture();
        f.Base.Scene.Page.PageSize = [220, 100];
        JObject cable = f.Cable;
        cable["core_count"] = 50;
        cable["points"] = JArray.Parse("[[0,16],[204,16]]");
        var cores = new JArray();
        for (int n = 1; n <= 50; n++)
        {
            var wire = new EplanMcp.Bridge.Core.Operations.CableDefinitionPlanner.Wire
            {
                Locator = new() { Project = "scratch-project", Page = ConnectionCreateFixture.PageName, Type = 9, Oid = n, Digest = new string('c', 64) },
                Points = [[n * 4, 8], [n * 4, 24]],
                Pins = [$"a{n}", $"b{n}"],
            };
            f.Inventory.Wires.Add(wire);
            f.Base.Scene.Graphical.Add(wire.Points);
            cores.Add(CableDefinitionFixture.Core(CableDefinitionFixture.Reference(wire), n.ToString(System.Globalization.CultureInfo.InvariantCulture), "BK"));
        }
        cable["cores"] = cores;
        f.Base.Request["items"] = new JArray(cable);
        Assert.Equal(50, ((JArray)f.Plan()["coreImpact"]!).Count);
    }

    [Fact]
    public void ExistingLocator_ReorderedMembersCannotBypassDuplicateAssignment()
    {
        var f = new CableDefinitionFixture();
        var wire = f.AddExisting();
        f.Cable["points"] = JArray.Parse("[[32,16],[48,16]]");
        f.Cores[0]!["connection"] = CableDefinitionFixture.Reference(wire);
        JObject reference = CableDefinitionFixture.Reference(wire);
        reference["locator"] = new JObject(((JObject)reference["locator"]!).Properties().Reverse());
        f.Cores.Add(CableDefinitionFixture.Core(reference, "PE", "GNYE"));
        Invalid(f, "/items");
    }

    [Fact]
    public void Scope_MissingInventoryOrAlreadyAssignedWire_FailsClosed()
    {
        var f = new CableDefinitionFixture();
        f.Base.Scene.ReadCables = null;
        Missing(f);
        f = new();
        var wire = f.AddExisting();
        wire.Cable = "-W9";
        f.Cable["points"] = JArray.Parse("[[32,16],[48,16]]");
        f.Cores[0]!["connection"] = CableDefinitionFixture.Reference(wire);
        Missing(f);
    }

    [Theory]
    [InlineData("[[16,4],[16,8]]")]
    [InlineData("[[8,8],[20,8]]")]
    [InlineData("[[16,4],[10000,4]]")]
    public void Definition_InvalidIntersectionOrBounds_Rejects(string points)
    {
        var f = new CableDefinitionFixture();
        f.Cable["points"] = JArray.Parse(points);
        Assert.Throws<BridgeErrorException>(() => f.Plan());
    }

    private static void Invalid(CableDefinitionFixture f, string pointer)
    {
        var error = Assert.Throws<BridgeErrorException>(() => f.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains(pointer, error.Metadata!["violations"], StringComparison.Ordinal);
    }
    private static void Missing(CableDefinitionFixture f) => Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeErrorException>(() => f.Plan()).Error.Reason);
}
