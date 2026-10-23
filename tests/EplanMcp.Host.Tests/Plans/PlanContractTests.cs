using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanContractTests
{
    [Fact]
    public void Record_AllParts_RoundTripWithoutLoss()
    {
        PlanDefinition definition = PlanTestData.Definition();
        PlanResult result = PlanResult.Aggregate([new(0, "page.update", true, ExecutionState.Committed)]) with
        {
            Backups = [new("backup", 1, 0.6m, true)],
            Verification = "skipped",
            Validation = PlanTestData.Json("[]"),
            Diff = PlanTestData.Json("[]"),
            Coverage = PlanTestData.Json("{}"),
        };
        PlanState state = new PlanState().Admit("attempt", "task", "host")
            .Approve(new(ApprovalDecision.Granted, "workbench", "binding"))
            .Start(PlanTestData.Created, PolicyVerdict.NeedsApproval).Complete(result, PlanTestData.Created);
        var record = new PlanRecord(1, "hash", definition, state);
        string json = JsonSerializer.Serialize(record, PlanJson.Options);

        PlanRecord copy = PlanJson.Read<PlanRecord>(json);

        Assert.Equal(json, JsonSerializer.Serialize(copy, PlanJson.Options));
        Assert.Contains("\"prediction\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"bindingRevision\":null", json, StringComparison.Ordinal);
        Assert.Equal(4, copy.State.Revision);
        Assert.Equal(PlanExecution.Committed, copy.State.Result!.Execution);
    }

    [Theory]
    [InlineData("\"extra\":true,")]
    [InlineData("\"schemaVersion\":1,")]
    [InlineData("\"schema\\u0056ersion\":1,")]
    public void Read_UnknownOrDuplicateRootMember_Rejects(string member)
    {
        string json = JsonSerializer.Serialize(PlanTestData.Definition(), PlanJson.Options);
        Assert.Throws<JsonException>(() => PlanJson.Read<PlanDefinition>("{" + member + json[1..]));
    }

    [Theory]
    [InlineData("\"language\":\"en_US\"", "\"language\":\"en_US\",\"language\":\"de_DE\"")]
    [InlineData("\"path\":\"PROJECT\"", "\"path\":\"PROJECT\",\"extra\":null")]
    [InlineData("\"effective\":true", "\"effective\":true,\"extra\":null")]
    [InlineData("\"tier\":\"write\"", "\"tier\":123")]
    [InlineData("\"tier\":\"write\"", "\"tier\":\"future\"")]
    [InlineData("\"planId\":\"0123456789abcdef0123456789abcdef\",", "")]
    [InlineData("\"cabinet\":\"default\"", "\"cabinet\":null")]
    [InlineData("\"tag\":\"objectHash\"", "\"tag\":\"future\"")]
    [InlineData("\"tag\":\"objectHash\"", "\"tag\":\"objectHash\",\"extra\":null")]
    [InlineData("\"tag\":\"objectHash\"", "\"tag\":\"objectHash\",\"tag\":\"pageHash\"")]
    public void Read_MalformedNestedContract_Rejects(string before, string after)
    {
        string json = JsonSerializer.Serialize(PlanTestData.Definition(), PlanJson.Options);
        Assert.Contains(before, json, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => PlanJson.Read<PlanDefinition>(json.Replace(before, after, StringComparison.Ordinal)));
    }

    [Fact]
    public void Enums_PlanHasExactlyFive_SingleStepStillHasFour()
    {
        string[] expected = ["not_started", "rolled_back", "committed", "partially_committed", "unknown"];
        Assert.Equal(expected, Enum.GetValues<PlanExecution>().Select(v => JsonSerializer.Serialize(v, PlanJson.Options).Trim('"')));
        Assert.Equal(4, typeof(ExecutionState).GetFields().Length);
        Assert.Throws<JsonException>(() => PlanJson.Read<PlanState>("""{"status":"expired"}"""));
    }

    [Fact]
    public void Preconditions_ClosedUnion_RoundTripsAllSixTags()
    {
        PlanDefinition definition = PlanTestData.Definition();
        PlanStep step = definition.Steps[0];
        definition = definition with
        {
            Steps = [step with { Preconditions =
        [
            new PlanObjectHash(step.ReadScope.Locators[0], "object"), new PlanPageHash(step.ReadScope.Locators[0], "page"),
            new PlanMembers(step.ReadScope, "members", 1), new PlanAbsent("page", "+A/2"),
            new PlanResourceHash("sha256"), new PlanBinding(2, "locks"),
        ] }]
        };
        string json = JsonSerializer.Serialize(definition, PlanJson.Options);
        PlanDefinition copy = PlanJson.Read<PlanDefinition>(json);
        Assert.Equal(definition.Steps[0].Preconditions.Select(p => p.GetType()), copy.Steps[0].Preconditions.Select(p => p.GetType()));
        Assert.Equal(json, JsonSerializer.Serialize(copy, PlanJson.Options));
    }
}
