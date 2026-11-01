using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class ApiTryOperationTests
{
    private static JObject HandleTarget() => new() { ["handle"] = "h:" + new string('a', 32), ["type"] = "Eplan.EplApi.DataModel.Pin" };

    [Fact]
    public void ReturnedEntryHandleKeepsSettingsReadOnlyAndViaParentsAsLocators()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Project.DeviceTagSettings.EnableSyntaxCheck");
        JObject target = HandleTarget();
        target["type"] = "Eplan.EplApi.DataModel.Project.DeviceTagSettings";
        var parameters = new JObject { ["target"] = target };
        args.Parameters = parameters.ToString();
        Assert.Equal("read", Assert.Single(Plan(args).Changes).Action);
        int inspections = _dispatch.Inspections;
        parameters["value"] = true;
        args.Parameters = parameters.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        parameters.Remove("value");
        parameters["via"] = "DeviceTagConfig";
        args.Parameters = parameters.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Equal(inspections, _dispatch.Inspections);
        Assert.Equal(0, _dispatch.Invocations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandleTargetScalarReadsAndWritesShareTheExistingSafetyPipeline(bool write)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Pin.ConsumerSources");
        var parameters = new JObject { ["target"] = HandleTarget() };
        if (write)
        { parameters["value"] = "new value"; }
        args.Parameters = parameters.ToString();
        args.Fingerprint = Plan(args).Fingerprint;
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Equal(write ? "new value" : "=A/1", data.Value);
        Assert.Equal(write ? ["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"] : Array.Empty<string>(), _transactions.Calls);
    }
    [Theory]
    [InlineData("extra")]
    [InlineData("uppercase")]
    [InlineData("short")]
    [InlineData("type")]
    [InlineData("unknown")]
    public void MalformedHandlesFailBeforeInspection(string defect)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Pin.ConsumerSources");
        JObject handle = HandleTarget();
        switch (defect)
        {
            case "extra":
                handle["project"] = "Scratch";
                break;
            case "uppercase":
                handle["handle"] = "h:" + new string('A', 32);
                break;
            case "short":
                handle["handle"] = "h:abc";
                break;
            case "type":
                handle["type"] = 1;
                break;
            case "unknown":
                handle["type"] = "Eplan.EplApi.Unknown";
                break;
        }
        args.Parameters = new JObject { ["target"] = handle }.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Equal(0, _dispatch.Inspections);
    }
    [Fact]
    public void HandleArgumentAndReturnedHandleUseOneWriteTransaction()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.ConnectionPointPosition.StoreToObject");
        JObject target = HandleTarget();
        target["type"] = "Eplan.EplApi.DataModel.ConnectionPointPosition";
        args.Parameters = new JObject { ["target"] = target }.ToString();
        _dispatch.MethodResult = new ApiObjectHandle { Handle = "h:" + new string('b', 32), Type = "Eplan.EplApi.DataModel.Pin" };
        args.Fingerprint = Plan(args).Fingerprint;
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Same(_dispatch.MethodResult, data.Handle);
        Assert.Equal(args.Group, data.HandleSource);
        Assert.Null(data.Value);
        Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }
    [Fact]
    public void PointDHandleAndCoordinatesBothMatchTheGeneratedArgumentShape()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Function.IsPointInsideLogicalArea");
        JObject p = JObject.Parse(args.Parameters);
        p["pnt"] = new JObject { ["handle"] = "h:" + new string('a', 32), ["type"] = "Eplan.EplApi.Base.PointD" };
        args.Parameters = p.ToString();
        Assert.Single(Plan(args).Changes);
    }
    [Fact]
    public void HandleMethodArgumentsAreValidatedAndReadWithoutAnUndoStep()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.Base.MultiLangString.IsEqual");
        JObject handle = HandleTarget();
        handle["type"] = "Eplan.EplApi.Base.MultiLangString";
        args.Parameters = new JObject { ["target"] = handle, ["otherVal"] = handle.DeepClone() }.ToString();
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.MethodResult = "True";
        Assert.Equal("True", ((ApiTryData)Executor().Execute(args)!).Value);
        Assert.Empty(_transactions.Calls);
        JObject parameters = JObject.Parse(args.Parameters);
        parameters["otherVal"] = "h:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        args.Parameters = parameters.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }
}
