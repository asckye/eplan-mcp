using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class ApiTryOperationTests
{
    [Fact]
    public void StaticReadHasNoTargetOrUndoButKeepsTheApprovedFingerprint()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.DocumentTypeManager.IsLogicalPageDocType");
        args.Parameters = "{\"nPageType\":\"Circuit\"}";
        WritePlan plan = Plan(args);
        Assert.Equal("read", Assert.Single(plan.Changes).Action);
        args.Fingerprint = plan.Fingerprint;
        _dispatch.MethodResult = "True";
        Assert.Equal("True", ((ApiTryData)Executor().Execute(args)!).Value);
        Assert.Empty(_transactions.Calls);
        Assert.Equal(1, _dispatch.Invocations);
        Assert.Equal(3, _dispatch.Inspections);
    }

    [Theory]
    [InlineData("Settings", true)]
    [InlineData("settings", false)]
    [InlineData("Project.Settings.Extra", false)]
    [InlineData("Settings()", false)]
    public void ProjectSettingEntryRequiresTheClosedCaseSensitivePath(string via, bool valid)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.ProjectSettings.GetBoolSetting");
        JObject p = JObject.Parse(args.Parameters);
        p["via"] = via;
        p["strSettingPath"] = "PROJECT.Test";
        p["nIdx"] = 0;
        args.Parameters = p.ToString();
        if (valid)
        { Assert.Equal("read", Assert.Single(Plan(args).Changes).Action); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason); }
        Assert.Equal(0, _dispatch.Invocations);
    }

    [Theory]
    [InlineData("Settings", false, false)]
    [InlineData("LayerTable.Layer", true, true)]
    [InlineData("LayerTable.Layer", false, false)]
    [InlineData("LayerTable.Layers", true, false)]
    public void IndexedProjectLayerNeedsExactlyOneStringSelector(string via, bool key, bool valid)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Graphics.GraphicalLayer.Name");
        JObject p = JObject.Parse(args.Parameters);
        p["via"] = via;
        if (key)
        { p["via_key"] = "EPLAN100"; }
        args.Parameters = p.ToString();
        if (valid)
        { Assert.Equal("read", Assert.Single(Plan(args).Changes).Action); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason); }
    }

    [Fact]
    public void ViaWriteSharesExactlyOneTransactionAndRejectsCrossProjectParents()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Page.Name");
        JObject p = JObject.Parse(args.Parameters);
        p["via"] = "Page";
        p["value"] = "renamed";
        args.Parameters = p.ToString();
        args.Fingerprint = Plan(args).Fingerprint;
        Assert.Equal("renamed", ((ApiTryData)Executor().Execute(args)!).Value);
        Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        p["target"]!["project"] = @"D:\real\Project";
        args.Parameters = p.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void SettingsSetterAndTargetOnAStaticCallAreRejectedBeforeInspecting()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Project.DeviceTagSettings.EnableSyntaxCheck");
        JObject p = JObject.Parse(args.Parameters);
        p["via"] = "DeviceTagConfig";
        p["value"] = true;
        args.Parameters = p.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        args = Args("O:Eplan.EplApi.DataModel.DocumentTypeManager.IsLogicalPageDocType");
        p = JObject.Parse(args.Parameters);
        p["nPageType"] = "Circuit";
        args.Parameters = p.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Equal(0, _dispatch.Inspections);
    }
}
