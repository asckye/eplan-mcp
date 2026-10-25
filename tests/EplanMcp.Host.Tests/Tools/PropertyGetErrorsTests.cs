using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PropertyGetErrorsTests
{
    [Theory]
    [InlineData(ErrorReason.InvalidArgument, "/queries/3")]
    [InlineData(ErrorReason.PolicyDenied, "/queries/3")]
    [InlineData(ErrorReason.AmbiguousName, "/queries/3/target")]
    [InlineData(ErrorReason.DeviceNotFound, "/queries/3/target")]
    [InlineData(ErrorReason.PageNotFound, "/queries/3/target")]
    [InlineData(ErrorReason.PlanStale, "/queries/3/target")]
    public void BridgeFailures_KeepReasonAndSafeDetailsAndIdentifyTheOriginalQuery(string reason, string path)
    {
        var source = new BridgeError
        {
            Reason = reason,
            Message = "Read a fresh target and retry.",
            Metadata = new()
            {
                ["check"] = "name",
            },
            Execution = ExecutionState.NotStarted,
        };
        JsonElement error = PropertyGetRequestTests.Error(PropertyGetErrors.FromBridge(source, 3));
        Assert.Equal(reason, error.GetProperty("reason").GetString());
        Assert.Equal(source.Message, error.GetProperty("message").GetString());
        Assert.Equal(path, error.GetProperty("metadata").GetProperty("pointer").GetString());
        Assert.Equal("name", error.GetProperty("metadata").GetProperty("check").GetString());
        Assert.False(source.Metadata.ContainsKey("pointer"));
        Assert.Equal(ExecutionState.NotStarted, source.Execution);
    }

    [Theory]
    [InlineData("", "/queries/2")]
    [InlineData("/target/projectObject", "/queries/2/target/project_object")]
    [InlineData("/target/deviceTag", "/queries/2/target/device_tag")]
    [InlineData("/properties/0/propertyId", "/queries/2/properties/0/property_id")]
    [InlineData("/properties/1/propertyName", "/queries/2/properties/1/property_name")]
    [InlineData("/properties/1/index", "/queries/2/properties/1/index")]
    [InlineData("/target/locator/oid", "/queries/2/target/locator/oid")]
    [InlineData("/target/a~0~1", "/queries/2/target/a~0~1")]
    [InlineData("/target/device_tag", "/queries/2/target/device_tag")]
    [InlineData("/target/propertyIdSuffix", "/queries/2/target/propertyIdSuffix")]
    [InlineData("/projectPath", "/project")]
    [InlineData("/language", "/language")]
    [InlineData("/detailed", "/response_format")]
    public void BridgeViolationPointers_AreRebasedWithoutChangingEscapesOrUnrelatedNames(string sourcePath, string expected)
    {
        var source = new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "Correct the selected fields.",
            Metadata = new()
            {
                ["violations"] = JsonSerializer.Serialize(new[]
                {
                    new PropertyViolation
                    {
                        Path = sourcePath,
                        Reason = ErrorReason.PolicyDenied,
                        Message = "This property is excluded.",
                    },
                    new PropertyViolation
                    {
                        Path = "/properties/2/index",
                        Message = "Supply an indexed property.",
                    },
                }, HostJson.Options),
            },
        };
        string original = source.Metadata["violations"];
        JsonElement error = PropertyGetRequestTests.Error(PropertyGetErrors.FromBridge(source, 2));
        PropertyViolation[] violations = PropertyGetRequestTests.Violations(error);
        Assert.Equal(2, violations.Length);
        Assert.Equal(expected, violations[0].Path);
        Assert.Equal(ErrorReason.PolicyDenied, violations[0].Reason);
        Assert.Equal("This property is excluded.", violations[0].Message);
        Assert.Equal("/queries/2/properties/2/index", violations[1].Path);
        Assert.Equal(ErrorReason.InvalidArgument, violations[1].Reason);
        Assert.Equal(original, source.Metadata["violations"]);
    }

    [Fact]
    public void AmbiguousNameCandidates_ArePreservedAsDetachedLocatorData()
    {
        ObjectLocator[] candidates = [.. Enumerable.Range(1, 20).Select(i => new ObjectLocator
        {
            Project = PropertyGetRequestTests.Project,
            Kind = "function",
            Type = 17,
            Oid = i,
            Name = "K1",
            Page = "P",
        })];
        string json = JsonSerializer.Serialize(candidates, HostJson.Options);
        var source = new BridgeError
        {
            Reason = ErrorReason.AmbiguousName,
            Message = "Select a candidate locator.",
            Metadata = new()
            {
                ["candidates"] = json,
            },
        };
        JsonElement error = PropertyGetRequestTests.Error(PropertyGetErrors.FromBridge(source, 5));
        string returned = error.GetProperty("metadata").GetProperty("candidates").GetString()!;
        Assert.Equal(json, returned);
        Assert.Equal(20, JsonSerializer.Deserialize<ObjectLocator[]>(returned, HostJson.Options)!.Length);
        Assert.Equal("/queries/5/target", error.GetProperty("metadata").GetProperty("pointer").GetString());
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("{}")]
    public void LegacyUnstructuredViolations_KeepTheErrorAndItsQueryPointer(string details)
    {
        var source = new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "Correct the selected query.",
            Metadata = new()
            {
                ["violations"] = details,
            },
        };
        JsonElement error = PropertyGetRequestTests.Error(PropertyGetErrors.FromBridge(source, 0));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Equal("/queries/0", error.GetProperty("metadata").GetProperty("pointer").GetString());
        Assert.False(error.GetProperty("metadata").TryGetProperty("violations", out _));
        Assert.Equal(details, source.Metadata["violations"]);
    }

    [Fact]
    public void NullViolationEntries_DoNotReplaceTheOriginalBridgeErrorWithAHostFailure()
    {
        var source = new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "Correct the selected query.",
            Metadata = new()
            {
                ["violations"] = "[null]",
            },
        };
        JsonElement error = PropertyGetRequestTests.Error(PropertyGetErrors.FromBridge(source, 0));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Equal("/queries/0", error.GetProperty("metadata").GetProperty("pointer").GetString());
        Assert.Empty(PropertyGetRequestTests.Violations(error));
    }

    [Fact]
    public void ReadInfrastructureErrors_PreserveRetryabilityWithoutWriteExecution()
    {
        var source = new BridgeError
        {
            Reason = ErrorReason.Timeout,
            Message = "Reduce the selected batch.",
            Retryable = true,
        };
        JsonElement error = PropertyGetRequestTests.Error(PropertyGetErrors.FromBridge(source, 1));
        Assert.Equal(ErrorReason.Timeout, error.GetProperty("reason").GetString());
        Assert.True(error.GetProperty("retryable").GetBoolean());
    }
}
