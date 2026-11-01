using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class ApiTryOperationTests
{
    [Fact]
    public void ArrayElementsUseMainDetachedJsonWithSourceAndNoUndoStep()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Pin.Connections");
        args.Parameters = new JObject { ["target"] = HandleTarget() }.ToString();
        _dispatch.MethodResult = new JArray(JObject.FromObject(new ApiObjectHandle { Handle = "h:" + new string('b', 32), Type = "Eplan.EplApi.DataModel.Pin" }, new Newtonsoft.Json.JsonSerializer { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() }));
        args.Fingerprint = Plan(args).Fingerprint;
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Equal(((JToken)_dispatch.MethodResult).ToString(Newtonsoft.Json.Formatting.None), data.ResultJson);
        Assert.Null(data.Handle);
        Assert.Null(data.Value);
        Assert.Equal(args.Group, data.HandleSource);
        Assert.Empty(_transactions.Calls);
    }
    [Theory]
    [InlineData("[{\"project\":\"Scratch\",\"page\":\"\",\"type\":1,\"oid\":1,\"digest\":\"identity\"}]")]
    [InlineData("{\"returnValue\":null,\"outputs\":{\"handle\":\"scalar output\"}}")]
    public void LocatorElementsAndNamedHandleScalarsDoNotClaimHandleAuditMetadata(string json)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Pin.Connections");
        args.Parameters = new JObject { ["target"] = HandleTarget() }.ToString();
        _dispatch.MethodResult = JToken.Parse(json);
        args.Fingerprint = Plan(args).Fingerprint;
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Null(data.HandleSource);
        Assert.Equal(JToken.Parse(json).ToString(Newtonsoft.Json.Formatting.None), data.ResultJson);
        Assert.Empty(_transactions.Calls);
    }
    [Theory]
    [InlineData("{\"id\":0}", true)]
    [InlineData("{\"name\":\"V1\"}", true)]
    [InlineData("{\"id\":\"0\"}", false)]
    [InlineData("{\"id\":32768}", false)]
    [InlineData("{\"id\":1.5}", false)]
    [InlineData("{\"id\":null}", false)]
    [InlineData("{\"id\":[]}", false)]
    [InlineData("{\"id\":0,\"value\":true}", false)]
    [InlineData("{\"id\":0,\"name\":\"V1\"}", false)]
    public void IndexedPropertiesUseExactReadOnlyKeyShapesBeforeInspection(string keys, bool valid)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Graphics.GraphicalLayerTable.Layer");
        JObject p = JObject.Parse(keys);
        JObject handle = HandleTarget();
        handle["type"] = "Eplan.EplApi.DataModel.Graphics.GraphicalLayerTable";
        p["target"] = handle;
        args.Parameters = p.ToString();
        if (valid)
        {
            Assert.Equal("read", Assert.Single(Plan(args).Changes).Action);
            Assert.Empty(_transactions.Calls);
        }
        else
        {
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
            Assert.Equal(0, _dispatch.Inspections);
        }
    }
    [Fact]
    public void ChainedConnectionHandleWriteStillUsesExactlyOneTransaction()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Connection.WriteProtected");
        JObject handle = HandleTarget();
        handle["type"] = "Eplan.EplApi.DataModel.Connection";
        args.Parameters = new JObject { ["target"] = handle, ["value"] = true }.ToString();
        args.Fingerprint = Plan(args).Fingerprint;
        Executor().Execute(args);
        Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }
}
