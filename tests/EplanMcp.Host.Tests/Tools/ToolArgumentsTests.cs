using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ToolArgumentsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("Line1", "Line1")]
    public void Optional_TreatsEmptyAsOmitted(string? value, string? expected)
    {
        Assert.Equal(expected, ToolArguments.Optional(value));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("concise", false)]
    [InlineData("detailed", true)]
    public void IsDetailed_AcceptsTheTwoFormatsAndNull(string? responseFormat, bool expected)
    {
        var violations = new List<string>();

        Assert.Equal(expected, ToolArguments.IsDetailed(responseFormat, violations));
        Assert.Empty(violations);
    }

    [Fact]
    public void IsDetailed_OtherValue_IsAViolation()
    {
        var violations = new List<string>();

        Assert.False(ToolArguments.IsDetailed("Detailed", violations));
        Assert.Equal(["'response_format' must be 'concise' or 'detailed'."], violations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zh_CN")]
    [InlineData("en_us")]
    public void CheckLanguage_AcceptsOmittedAndLanguageCodes(string? language)
    {
        var violations = new List<string>();

        ToolArguments.CheckLanguage(language, violations);

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("zh")]
    [InlineData("zh-CN")]
    [InlineData("zh_CN;de_DE")]
    [InlineData("Chinese")]
    public void CheckLanguage_OtherShapes_AreAViolation(string language)
    {
        var violations = new List<string>();

        ToolArguments.CheckLanguage(language, violations);

        Assert.Contains("'language' must be a language code", Assert.Single(violations), StringComparison.Ordinal);
    }

    [Fact]
    public void PropertyResult_CleansNameAndValueAndKeepsNull()
    {
        List<PropertyResult>? results = PropertyResult.From(
            [new PropertyEntry { Id = 10000, Name = "Project‮name", Value = "Line\u00001" }, new PropertyEntry { Index = 2, Name = "UserProp", Value = "x" }]);

        Assert.Null(PropertyResult.From(null));
        Assert.NotNull(results);
        Assert.Equal((10000, (int?)null, "Projectname", "Line1"), (results[0].Id!.Value, results[0].Index, results[0].Name, results[0].Value));
        Assert.Equal(((int?)null, (int?)2), (results[1].Id, results[1].Index));
    }
}
