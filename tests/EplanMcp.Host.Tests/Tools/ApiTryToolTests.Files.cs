using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class ApiTryToolTests
{
    private const string FileGroup = "O:Eplan.EplApi.HEServices.PrePlanningService.ExportPropertyDefinitions";
    private string FileParameters(object file)
    {
        var locator = new DrawingLocator { Project = _harness.ProjectPath, Page = "", Type = 1, Oid = 1, Digest = new string('a', 64) };
        return JsonSerializer.Serialize(new { target = locator, pProject = locator, strFileName = file }, EplanMcp.Host.HostJson.Options);
    }

    [Theory]
    [InlineData("../output.xml")]
    [InlineData("/output.xml")]
    [InlineData("C:\\output.xml")]
    [InlineData("\\\\server\\output.xml")]
    [InlineData("CON.xml")]
    [InlineData("folder/output.xml")]
    [InlineData("output.xml ")]
    public async Task OutputEscapesNeverReachPlanningOrApproval(string file)
    {
        Assert.Equal(ErrorReason.InvalidArgument, WriteToolHarness.Error(await Call(parameters: FileParameters(file), group: FileGroup)).GetProperty("reason").GetString());
        Assert.DoesNotContain(_harness.Bridge.Calls, c => c.Operation == OperationNames.ApiTryPlan);
        Assert.Empty(_approval.Questions);
    }

    [Fact]
    public async Task FilePlanRetainsRawPathsAndBindsTrustedHostLocationsPerCall()
    {
        string raw = FileParameters("definitions.xml");
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = a =>
        {
            var args = (ApiTryArgs)a!;
            Assert.Equal(_harness.Locations.Root, args.DataRoot);
            Assert.Matches("^[0-9a-f]{32}$", args.OutputId);
            Assert.Equal(raw, args.Parameters);
            return new ApiTryData { Files = [new() { Path = "api-try-" + args.OutputId + "/definitions.xml", Bytes = 4, Sha256 = new string('a', 64) }] };
        };
        JsonElement result = WriteToolHarness.Structured(await Call(parameters: raw, group: FileGroup));
        JsonElement artifact = Assert.Single(result.GetProperty("files").EnumerateArray());
        Assert.Equal(4, artifact.GetProperty("bytes").GetInt64());
        Assert.Equal(new string('a', 64), artifact.GetProperty("sha256").GetString());
        WriteToolHarness.Structured(await Call(parameters: raw, group: FileGroup));
        Assert.Equal(2, _approval.Questions.Count);
        Assert.All(_approval.Questions, question => Assert.Equal(raw, question.Plan.Details!["rawParameters"]));
    }
}
