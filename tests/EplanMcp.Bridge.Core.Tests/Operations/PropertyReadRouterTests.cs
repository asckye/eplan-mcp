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
            args["target"]![field[7..]] = field == "target.deviceTag" ? new JValue("-K1") : new JValue(17);
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
}
