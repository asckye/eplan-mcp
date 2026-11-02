using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ApiRefusalTemplateTests
{
    [Theory]
    [InlineData("Function.UpdateLocalTemplate", "method")]
    [InlineData("MacroBox.RepresentationType", "property")]
    [InlineData("Page.ConvertToGraphics", "method")]
    public void GetShowsRefusalWithoutAnExecutableWriteExample(string member, string kind)
    {
        string group = "O:Eplan.EplApi.DataModel." + member;
        var entry = new ApiEntry(new(group, group, kind, member, "registered", null, [], false),
            "Eplan.EplApi.DataModel", "T:Eplan.EplApi.DataModel." + member.Split('.')[0], member.Split('.')[1], null, [group], []);
        var index = new ApiIndex(new([entry], new(StringComparer.Ordinal)), "refusal-test");
        var result = ApiTools.Get(index, group, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsError == true);
        JsonElement[] templates = [.. result.StructuredContent!.Value.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("try_template"))];
        foreach (JsonElement write in templates.Where(t => t.GetProperty("access").GetString() == "write"))
        {
            Assert.False(write.GetProperty("dispatchable").GetBoolean());
            Assert.Equal("refused_unsafe", write.GetProperty("refusal_reason").GetString());
            Assert.Equal(ErrorReason.CapabilityMissing, write.GetProperty("reason").GetString());
            Assert.Contains("api-notes A77", write.GetProperty("exclusion_reason").GetString(), StringComparison.Ordinal);
            Assert.False(write.TryGetProperty("arguments", out _));
            Assert.False(write.TryGetProperty("example", out _));
        }
        Assert.Contains(templates, t => t.GetProperty("access").GetString() == "write");
        if (kind == "property")
        { Assert.True(Assert.Single(templates, t => t.GetProperty("access").GetString() == "read").GetProperty("dispatchable").GetBoolean()); }
    }
}
