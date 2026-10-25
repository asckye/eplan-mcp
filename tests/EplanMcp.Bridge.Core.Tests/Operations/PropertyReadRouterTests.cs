using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class PropertyReadRouterTests
{
    [Fact]
    public void Invoke_ReadContract_HostJsonThroughRouterAndBackContainsOnlyDetachedRows()
    {
        var adapter = new FakePropertyReadAdapter();
        adapter.Properties.Add(11011, PropertyReadTextTests.Multilingual());
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter([new PropertyReadOperation(adapter)], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(30));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var args = new PropertyReadArgs
        {
            ProjectPath = "resolved-project",
            Target = new PropertyTarget(page: "=AP+ST1/3"),
            Properties = [new PropertySelector(propertyId: 11011), new PropertySelector(propertyId: 3012)],
            Language = "de_DE",
            Detailed = true,
        };

        JToken result = router.Invoke(OperationNames.PropertyRead, JToken.Parse(JsonSerializer.Serialize(args, options)), "read-request");
        PropertyReadData data = JsonSerializer.Deserialize<PropertyReadData>(result.ToString(), options)!;

        Assert.Equal(1, dispatcher.Calls);
        Assert.Equal("resolved-project", adapter.ProjectPath);
        Assert.Equal("=AP+ST1/3", adapter.Target!.Page);
        Assert.Equal(2, data.Items.Count);
        Assert.Equal("value", data.Items[0].Status);
        Assert.Equal("German text", data.Items[0].Value!.Text);
        Assert.Equal("de_DE", data.Items[0].Language);
        Assert.False(data.Items[0].Fallback);
        Assert.Equal("multi_language", data.Items[0].Definition!.ValueType);
        Assert.False(data.Items[0].Definition!.IsInternal);
        Assert.Equal("read_refused", data.Items[1].Status);
        Assert.Null(data.Items[1].Definition);
        Assert.Equal(1, adapter.PropertyOpens);
        Assert.Contains(OperationNames.PropertyRead, router.SupportedOperations);
        Assert.Null(data.NextSelector);
        adapter.AssertDisposed();
        Assert.Equal(result, BridgeJson.FromObject(data));
    }

    [Fact]
    public void Invoke_CancelDuringGetter_ReturnsCancelledWithoutWriteOutcome()
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter();
        var router = new OperationRouter([new PropertyReadOperation(adapter)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(30));
        adapter.OnAccess = stage =>
        {
            if (stage == "stored")
            {
                JToken cancelled = router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "read-request" }, "cancel-request");
                Assert.True((bool)cancelled["found"]!);
            }
        };

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.PropertyRead,
            BridgeJson.FromObject(PropertyReadOperationTests.Args()), "read-request"));

        Assert.Equal(ErrorReason.Cancelled, error.Error.Reason);
        Assert.Null(error.Error.Execution);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("target.unknown")]
    [InlineData("target.deviceTag")]
    [InlineData("target.page")]
    [InlineData("target.projectObject")]
    [InlineData("projectPath")]
    [InlineData("language")]
    [InlineData("detailed")]
    [InlineData("selectorOffset")]
    [InlineData("indexOffset")]
    [InlineData("propertyId")]
    [InlineData("propertyName")]
    [InlineData("index")]
    [InlineData("selector.unknown")]
    public void Invoke_UnknownFieldsOrCoercedTypes_AreRejectedBeforeDispatch(string field)
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter();
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter([new PropertyReadOperation(adapter)], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(30));
        JObject args = (JObject)BridgeJson.FromObject(PropertyReadOperationTests.Args());
        if (field.StartsWith("target.", StringComparison.Ordinal))
        {
            args["target"]![field[7..]] = new JValue(17);
        }
        else if (field.StartsWith("selector.", StringComparison.Ordinal))
        {
            args["properties"]![0]![field[9..]] = true;
        }
        else if (field is "index" or "propertyId" or "propertyName")
        {
            args["properties"]![0]![field] = "10013";
        }
        else
        {
            args[field] = field == "projectPath" || field == "language" ? new JValue(17) : new JValue("false");
        }

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.PropertyRead, args, "invalid"));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal(0, dispatcher.Calls);
        Assert.Equal(0, adapter.Opens);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("function")]
    [InlineData("connection")]
    [InlineData("page")]
    public void Invoke_NewTargetContracts_RoundTripThroughBothSerializers(string kind)
    {
        bool connection = kind == "connection";
        int id = connection ? 31002 : kind == "page" ? 11011 : 20011;
        var adapter = new FakePropertyReadAdapter { ObjectType = connection ? "Connection" : kind == "page" ? "Page" : "Function" };
        adapter.Properties.Add(id, new FakeProperty());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ObjectLocator locator = PropertyReadTargetTests.Locator(PropertyReadTargetTests.Function());
        locator.Kind = kind;
        PropertyReadArgs args = PropertyReadOperationTests.Args(id, 3012);
        args.Target = kind == "device" ? new PropertyTarget(deviceTag: "=AP-K1", page: "=AP/1") : new PropertyTarget(locator: locator);
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter([new PropertyReadOperation(adapter)], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(30));

        JToken json = router.Invoke(OperationNames.PropertyRead, JToken.Parse(JsonSerializer.Serialize(args, options)), "new-target");
        PropertyReadData result = JsonSerializer.Deserialize<PropertyReadData>(json.ToString(), options)!;

        Assert.Equal(1, dispatcher.Calls);
        Assert.Equal("value", result.Items[0].Status);
        Assert.Equal("read_refused", result.Items[1].Status);
        Assert.Equal(JsonSerializer.Serialize(args.Target, options), JsonSerializer.Serialize(result.Items[0].Target, options));
        Assert.Equal(json, BridgeJson.FromObject(result));
        Assert.Equal(1, adapter.PropertyOpens);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("unknown", true)]
    [InlineData("oid", "1")]
    [InlineData("type", 1.5)]
    [InlineData("name", 17)]
    [InlineData("kind", "device")]
    [InlineData("project", 17)]
    [InlineData("pos", "NaN,20")]
    public void Invoke_MalformedLocator_IsRejectedBeforeDispatch(string field, object value)
    {
        var adapter = new FakePropertyReadAdapter();
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter([new PropertyReadOperation(adapter)], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(30));
        PropertyReadArgs input = PropertyReadOperationTests.Args(20011);
        input.Target = new PropertyTarget(locator: PropertyReadTargetTests.Locator(PropertyReadTargetTests.Function()));
        JObject args = (JObject)BridgeJson.FromObject(input);
        args["target"]!["locator"]![field] = JToken.FromObject(value);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.PropertyRead, args, "bad-locator"));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal(0, dispatcher.Calls);
        Assert.Equal(0, adapter.Opens);
    }

    [Theory]
    [InlineData("ambiguous", ErrorReason.AmbiguousName)]
    [InlineData("absent", ErrorReason.DeviceNotFound)]
    [InlineData("stale", ErrorReason.PlanStale)]
    public void Invoke_ResolutionRefusal_PreservesReadErrorAndNeverOpensPropertyScope(string scenario, string reason)
    {
        var adapter = new ResolvingAdapter();
        var router = new OperationRouter([new PropertyReadOperation(adapter)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(30));
        PropertyReadArgs args = PropertyReadOperationTests.Args(20011);
        ObjectLocator stale = PropertyReadTargetTests.Locator(PropertyReadTargetTests.Function());
        stale.Name += "-changed";
        args.Target = scenario == "stale" ? new PropertyTarget(locator: stale)
            : new PropertyTarget(deviceTag: scenario == "absent" ? "=AP-missing" : "=AP-K1");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.PropertyRead,
            JToken.Parse(JsonSerializer.Serialize(args, options)), "refusal"));
        BridgeError copy = JsonSerializer.Deserialize<BridgeError>(BridgeJson.FromObject(error.Error).ToString(), options)!;

        Assert.Equal(reason, copy.Reason);
        Assert.Null(copy.Execution);
        Assert.Equal(0, adapter.Values.Opens);
        if (scenario == "ambiguous")
        {
            ObjectLocator[] candidates = JsonSerializer.Deserialize<ObjectLocator[]>(copy.Metadata!["candidates"], options)!;
            Assert.Equal([1L, 2L], candidates.Select(c => c.Oid));
        }
    }

    private sealed class ResolvingAdapter : IPropertyReadAdapter
    {
        public FakePropertyReadAdapter Values { get; } = new();

        public IPropertyReadScope Open(string projectPath, PropertyTarget target)
        {
            SnapshotObject[] objects = [PropertyReadTargetTests.Function(), PropertyReadTargetTests.Function(2)];
            if (target.Locator is { } locator)
            {
                PropertyReadTargets.Located(projectPath, locator, objects, o => o);
            }
            else
            {
                PropertyReadTargets.Main(projectPath, target, objects, o => o);
            }

            return Values.Open(projectPath, target);
        }
    }
}
