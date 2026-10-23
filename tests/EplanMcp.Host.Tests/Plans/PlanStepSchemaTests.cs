using System.Text.Encodings.Web;
using System.Text.Json;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanStepSchemaTests
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    [Fact]
    public async Task InputSchema_MatchesReviewedSnapshot()
    {
        await Verify(JsonSerializer.Serialize(PlanStepCatalog.InputSchema, _options) + "\n", "json")
            .UseDirectory("../../Contracts.Snapshots").UseFileName("plan-step-input");
    }
}
