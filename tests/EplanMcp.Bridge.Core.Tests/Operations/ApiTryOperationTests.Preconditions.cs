using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class ApiTryOperationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportUpdate_RequiresPlacedReportWithoutDemandingPageOverload(bool placed)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.HEServices.Reports.Update");
        JObject p = JObject.Parse(args.Parameters);
        p["oReportBlockRef"] = p["target"]!.DeepClone();
        args.Parameters = p.ToString();
        _dispatch.TargetCheck = a => CheckPage(a, placed);
        if (placed)
        {
            args.Fingerprint = Plan(args).Fingerprint;
            Executor().Execute(args);
            Assert.Equal(1, _dispatch.Invocations);
        }
        else
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
            Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
            Assert.Equal(0, _dispatch.Invocations);
            Assert.Empty(_transactions.Calls);
        }
    }

    [Theory]
    [InlineData("CanDeviceTagBeNested", "pFunction")]
    [InlineData("FindNameGivingFunction", "pActual")]
    [InlineData("FindNameGivingObject", "f")]
    [InlineData("UpdateConnectionDefinitionPointsParent", "pCDP")]
    [InlineData("AdjustFullName", "pFunctionBase")]
    [InlineData("AdjustVisibleName", "pFunctionBase")]
    [InlineData("EvaluateAndSetAllNames", null)]
    [InlineData("EvaluateAndSetAllVisibleNames", null)]
    public void PageContextTable_RejectsMissingPageBeforeInvocationOrTransaction(string member, string? parameter)
    {
        ApiTryArgs args = NameArgs(member, parameter, explicitPage: false);
        _dispatch.TargetCheck = CheckPage;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
        Assert.Equal(0, _dispatch.Invocations);
        Assert.Equal(0, _dispatch.Inspections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanNest_UnplacedFunctionIsRejectedEvenWithExplicitPage(bool explicitPage)
    {
        ApiTryArgs args = NameArgs("CanDeviceTagBeNested", "pFunction", explicitPage);
        _dispatch.TargetCheck = a => CheckPage(a, placed: false);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("placed object", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _dispatch.Invocations);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void CanNest_ExplicitPageAndPlacedFunction_InvokeNormally()
    {
        ApiTryArgs args = NameArgs("CanDeviceTagBeNested", "pFunction", explicitPage: true);
        _dispatch.TargetCheck = CheckPage;
        args.Fingerprint = Plan(args).Fingerprint;
        Executor().Execute(args);
        Assert.Equal(1, _dispatch.Invocations);
        Assert.Empty(_transactions.Calls);
    }

    private static ApiTryArgs NameArgs(string member, string? parameter, bool explicitPage)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.HEServices.NameService." + member);
        JObject p = JObject.Parse(args.Parameters);
        if (parameter is not null)
        { p[parameter] = p["target"]!.DeepClone(); }
        if (explicitPage)
        { p["pPage"] = p["target"]!.DeepClone(); }
        args.Parameters = p.ToString();
        return args;
    }

    private static void CheckPage(ApiTryArgs args) => CheckPage(args, placed: true);
    private static void CheckPage(ApiTryArgs args, bool placed)
    {
#pragma warning disable CA2000 // Borrowed fake function and page must not be disposed.
        var function = new Function(1) { Page = placed ? new Page() : null! };
#pragma warning restore CA2000
        JObject p = JObject.Parse(args.Parameters);
        string[] shape = ApiTryCatalog.Shape(args.Group, p.Properties().Select(v => v.Name))!;
        ApiTryPreconditions.Check(args.Group, shape, _ => EplanNativePreconditions.HasPage(function));
    }
}
