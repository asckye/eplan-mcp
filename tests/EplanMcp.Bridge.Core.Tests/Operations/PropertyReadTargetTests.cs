using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyReadTargetTests
{
    internal static SnapshotObject Function(long oid = 1, string? page = "=AP/1", bool main = true) => new()
    {
        Kind = "function",
        Type = 17,
        Oid = oid,
        Name = "=AP-K1",
        Page = page,
        Main = main,
        Category = "Coil",
        Pos = "10,20",
    };

    internal static ObjectLocator Locator(SnapshotObject obj) => new()
    {
        Project = "resolved-project",
        Kind = obj.Kind,
        Type = obj.Type,
        Oid = obj.Oid,
        Name = obj.Name,
        Page = obj.Page,
        Category = obj.Category,
        Pos = obj.Pos,
    };

    [Fact]
    public void Main_ExactFullTag_DeduplicatesIdentityAndExcludesAuxiliaries()
    {
        SnapshotObject main = Function();
        SnapshotObject shorter = Function(3);
        shorter.Name = "-K1";
        SnapshotObject wrongCase = Function(4);
        wrongCase.Name = "=AP-k1";

        SnapshotObject found = PropertyReadTargets.Main("resolved-project", new PropertyTarget(deviceTag: "=AP-K1"),
            new[] { Function(2, main: false), shorter, main, Function(), wrongCase }, o => o);

        Assert.Same(main, found);
    }

    [Fact]
    public void Main_PageQualifier_SelectsOnlyThatMainFunction()
    {
        SnapshotObject expected = Function(2, "=AP/2");
        SnapshotObject actual = PropertyReadTargets.Main("resolved-project", new PropertyTarget(deviceTag: "=AP-K1", page: "=AP/2"),
            new[] { Function(), expected, Function(3, null) }, o => o);

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Main_PageLessMain_IsAddressableWithoutAPageQualifier()
    {
        SnapshotObject main = Function(page: null);

        Assert.Same(main, PropertyReadTargets.Main("resolved-project", new PropertyTarget(deviceTag: main.Name), new[] { main }, o => o));
    }

    [Fact]
    public void Main_PageLessAndPlacedMainWithSameFullName_AreAmbiguous()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => PropertyReadTargets.Main("resolved-project",
            new PropertyTarget(deviceTag: "=AP-K1"), new[] { Function(), Function(2, page: null) }, o => o));

        Assert.Equal(ErrorReason.AmbiguousName, error.Error.Reason);
        Assert.Equal(2, JArray.Parse(error.Error.Metadata!["candidates"]).Count);
    }

    [Theory]
    [InlineData("-K1", null)]
    [InlineData("=AP-k1", null)]
    [InlineData("=AP-K1", "=AP/absent")]
    public void Main_NoExactMain_ReturnsDeviceNotFound(string tag, string? page)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => PropertyReadTargets.Main("resolved-project",
            new PropertyTarget(deviceTag: tag, page: page), new[] { Function(), Function(2, main: false) }, o => o));

        Assert.Equal(ErrorReason.DeviceNotFound, error.Error.Reason);
    }

    [Fact]
    public void Main_MultipleRepresentationsOnSamePage_RefusesAndOffersAtMost20Locators()
    {
        SnapshotObject[] functions = [.. Enumerable.Range(1, 25).Select(i => Function(i))];
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => PropertyReadTargets.Main("resolved-project",
            new PropertyTarget(deviceTag: "=AP-K1", page: "=AP/1"), functions.Reverse(), o => o));

        Assert.Equal(ErrorReason.AmbiguousName, error.Error.Reason);
        ObjectLocator[] candidates = JArray.Parse(error.Error.Metadata!["candidates"]).ToObject<ObjectLocator[]>()!;
        Assert.Equal(20, candidates.Length);
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i), candidates.Select(c => c.Oid));
        Assert.All(candidates, c =>
        {
            Assert.Equal("function", c.Kind);
            Assert.Equal("resolved-project", c.Project);
            Assert.Equal("=AP-K1", c.Name);
            Assert.Equal("Coil", c.Category);
            Assert.Equal("10,20", c.Pos);
        });
    }

    [Theory]
    [InlineData("project")]
    [InlineData("type")]
    [InlineData("oid")]
    [InlineData("name")]
    [InlineData("page")]
    [InlineData("category")]
    [InlineData("position")]
    [InlineData("kind")]
    public void Located_AnyIdentityMismatch_RefusesWithoutResolvingByName(string field)
    {
        SnapshotObject obj = Function();
        ObjectLocator locator = Locator(obj);
        switch (field)
        {
            case "project":
                locator.Project = "another-project";
                break;
            case "type":
                locator.Type++;
                break;
            case "oid":
                locator.Oid++;
                break;
            case "name":
                locator.Name += "-changed";
                break;
            case "page":
                locator.Page += "-changed";
                break;
            case "category":
                locator.Category = "Motor";
                break;
            case "position":
                locator.Pos = "10.01,20";
                break;
            case "kind":
                locator.Kind = "connection";
                break;
        }

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => PropertyReadTargets.Located("resolved-project",
            locator, new[] { obj, Function(99) }, o => o));

        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal(field is "oid" or "kind" ? "not_found" : field, error.Error.Metadata!["check"]);
    }

    [Fact]
    public void Located_MissingOidWithSameName_Refuses()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => PropertyReadTargets.Located("resolved-project",
            Locator(Function()), new[] { Function(99) }, o => o));

        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
    }

    [Fact]
    public void Located_CrossProject_RefusesBeforeDescribingAnyObject()
    {
        ObjectLocator locator = Locator(Function());
        locator.Project = "another-project";
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => PropertyReadTargets.Located("resolved-project",
            locator, new[] { Function() }, _ => throw new InvalidOperationException("No identity getter may run.")));

        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal("project", error.Error.Metadata!["check"]);
    }

    [Fact]
    public void Located_ExactTypeAfterOtherTypeWithSameOid_UsesExactObjectAndPositionTolerance()
    {
        SnapshotObject wrongType = Function();
        wrongType.Type++;
        SnapshotObject correct = Function();
        ObjectLocator locator = Locator(correct);
        locator.Pos = "10.001,20";

        Assert.Same(correct, PropertyReadTargets.Located("resolved-project", locator, new[] { wrongType, correct }, o => o));
    }

    [Theory]
    [InlineData("Function", 20011, "value")]
    [InlineData("Connection", 31002, "value")]
    [InlineData("Function", 31002, "read_refused")]
    [InlineData("Connection", 20011, "read_refused")]
    [InlineData("Cable", 20011, "read_refused")]
    [InlineData("TerminalStrip", 20011, "value")]
    [InlineData("UnknownFunctionSubtype", 20011, "read_refused")]
    public void Read_ResolvedDictionaryType_ClassifiesBeforeAnyPropertyGetter(string objectType, int id, string status)
    {
        var adapter = new FakePropertyReadAdapter { ObjectType = objectType };
        adapter.Properties.Add(id, new FakeProperty());
        PropertyReadArgs args = PropertyReadOperationTests.Args(id);
        args.Target = objectType == "Connection"
            ? new PropertyTarget(locator: new ObjectLocator { Project = args.ProjectPath, Kind = "connection", Type = 40, Oid = 1 })
            : new PropertyTarget(deviceTag: "=AP-K1");
        if (status == "read_refused")
        {
            adapter.OnAccess = stage => Assert.False(stage is "property.lookup" or "metadata" or "is_empty" or "stored" or "display" or "text");
        }

        Assert.Equal(status, Assert.Single(PropertyReadOperationTests.Read(adapter, args).Items).Status);
        Assert.Equal(status == "value" ? 1 : 0, adapter.PropertyOpens);
        adapter.AssertDisposed();
    }
}
