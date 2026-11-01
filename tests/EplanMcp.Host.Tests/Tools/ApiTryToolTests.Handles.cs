using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
namespace EplanMcp.Host.Tests.Tools;

public sealed partial class ApiTryToolTests
{
    [Fact]
    public async Task HandleOutputAndApprovalAuditContainOnlyTypeAndSource()
    {
        const string type = "Eplan.EplApi.DataModel.Pin";
        _harness.Bridge.Handlers[OperationNames.ApiTryPlan] = a =>
        {
            var args = (ApiTryArgs)a!;
            Assert.NotEmpty(args.HostSession);
            Assert.Equal(ScratchPaths.Canonical(_harness.ProjectPath), args.ScratchPath);
            WritePlan plan = _harness.Plan([new() { Action = "read", Kind = "unverified API", Name = Group, Property = "Pin.Name" }]);
            plan.Details = new() { ["status"] = "dispatchable", ["unverified"] = "true", ["handles"] = type + " returned by O:Connection.StartPin" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = a =>
        {
            Assert.NotEmpty(((ApiTryArgs)a!).HostSession);
            return new ApiTryData { Handle = new() { Handle = "h:" + new string('b', 32), Type = type }, HandleSource = Group };
        };
        JsonElement result = WriteToolHarness.Structured(await Call(parameters: "{\"target\":{\"handle\":\"h:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"type\":\"" + type + "\"}}"));
        Assert.Equal(type, result.GetProperty("value").GetProperty("type").GetString());
        Assert.Contains(_approval.Questions.Single().Changes, line => line.Contains(type + " returned by O:Connection.StartPin", StringComparison.Ordinal));
        string audit = File.ReadAllText(Assert.Single(Directory.GetFiles(_harness.Locations.Audit, "*.jsonl")));
        Assert.Contains("\"unverified\":true", audit, StringComparison.Ordinal);
        Assert.Contains(type + " returned by O:Connection.StartPin", audit, StringComparison.Ordinal);
        Assert.Contains("\"handleSource\":\"" + Group, audit, StringComparison.Ordinal);
    }
}
