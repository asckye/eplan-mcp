using System.Text;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class TranslationTableTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf16BomAndTabs_AreParsedWithoutChangingText(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, true, true);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes("zh_CN\ten_US\r\n电机\tMotor\r\n")];

        TranslationTable table = TranslationTable.Parse(bytes);

        Assert.Equal(["zh_CN", "en_US"], table.Columns);
        Assert.Equal(["电机", "Motor"], Assert.Single(table.Rows));
        Assert.Equal("en_US", table.ReferenceLanguage);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("utf8")]
    [InlineData("no bom")]
    [InlineData("odd byte")]
    [InlineData("surrogate")]
    public void InvalidEncoding_ReportsTheFileParameter(string kind)
    {
        byte[] bytes = kind switch
        {
            "empty" => [],
            "utf8" => Encoding.UTF8.GetBytes("en_US\nMotor"),
            "no bom" => Encoding.Unicode.GetBytes("en_US\nMotor"),
            "odd byte" => [0xFF, 0xFE, 0x65],
            _ => [0xFF, 0xFE, 0x00, 0xD8],
        };

        BridgeError error = Invalid(() => TranslationTable.Parse(bytes));

        Assert.Equal("/file", Assert.Single(JArray.Parse(error.Metadata!["violations"])).Value<string>("path"));
    }

    [Theory]
    [InlineData("en_US,en_US\nMotor,Motor")]
    [InlineData("English\nMotor")]
    [InlineData("en_US\ten_US\nMotor\tMotor")]
    [InlineData("en_US\t\nMotor\t")]
    [InlineData("en_US\tCOMMENT\nMotor\tcomment")]
    [InlineData("en_US\tzh_CN\nMotor")]
    [InlineData("en_US\nMotor\textra")]
    [InlineData("en_US\n")]
    public void BadHeadersAndRows_UsePointersIntoTheToolInput(string text)
    {
        BridgeError error = Invalid(() => Parse(text));

        Assert.Contains(JArray.Parse(error.Metadata!["violations"]), p => p.Value<string>("path") == "/file");
        Assert.All(JArray.Parse(error.Metadata["violations"]), p => Assert.True(p.Value<string>("path") is "/file" or "/reference_language"));
    }

    [Theory]
    [InlineData("zh_CN")]
    [InlineData("en_US;zh_CN")]
    [InlineData("")]
    [InlineData(" en_US")]
    public void ReferenceLanguage_MustBeOneExactColumn(string referenceLanguage)
    {
        BridgeError error = Invalid(() => Parse("en_US\nMotor", referenceLanguage));

        Assert.Equal("/reference_language", Assert.Single(JArray.Parse(error.Metadata!["violations"])).Value<string>("path"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyReferenceCell_ReportsItsFileLine(string text)
    {
        BridgeError error = Invalid(() => Parse("zh_CN\ten_US\n电机\t" + text));
        JToken problem = Assert.Single(JArray.Parse(error.Metadata!["violations"]));

        Assert.Equal("/file", problem.Value<string>("path"));
        Assert.Contains("Line 2", problem.Value<string>("message"), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMalformedRow_IsReportedUpFront()
    {
        BridgeError error = Invalid(() => Parse("en_US\tzh_CN\n\t电机\n\t马达\nMotor\t电机\textra"));

        Assert.Equal(3, JArray.Parse(error.Metadata!["violations"]).Count);
    }

    [Fact]
    public void Remapping_UsesTheExactExportHeaderAndLeavesAbsentColumnsEmpty()
    {
        TranslationTable file = Parse("zh_CN\ten_US\n电机\tMotor\n阀\tValve");
        TranslationTable export = Parse("de_DE\ten_US\tzh_CN\tCOMMENT\nMotor\tMotor\t电机\tkeep", export: true);

        string remapped = file.Remap(export);

        Assert.Equal("de_DE\ten_US\tzh_CN\tCOMMENT\r\n\tMotor\t电机\t\r\n\tValve\t阀\t\r\n", remapped);
    }

    [Fact]
    public void UnknownColumns_ReportAllFileColumnsAsMetadata()
    {
        TranslationTable file = Parse("en_US\tzh_CN\nMotor\t电机");
        TranslationTable export = Parse("en_US\tde_DE\tCOMMENT", export: true);

        BridgeError error = Invalid(() => file.Remap(export));

        Assert.Equal(["en_US", "zh_CN"], JArray.Parse(error.Metadata!["columns"]).Values<string>());
        Assert.Equal("/file", Assert.Single(JArray.Parse(error.Metadata["violations"])).Value<string>("path"));
    }

    [Fact]
    public void ComparingExports_CountsAddedEntriesAndFilledOrKeptCells()
    {
        TranslationTable file = Parse("en_US\tzh_CN\tde_DE\nNew\t新\tNeu\nFill\t填\tFüllen\nKeep\t新译文\tNeu\nSame\t相同\tGleich\nBlank\t\t");
        TranslationTable before = Parse("de_DE\ten_US\tzh_CN\tCOMMENT\n\tFill\t\tunchanged\nAlt\tKeep\t原译文\t\nGleich\tSame\t相同\t\nAlt\tBlank\t原文\t", export: true);
        TranslationTable after = Parse("zh_CN\tde_DE\ten_US\tCOMMENT\n新\tNeu\tNew\t\n填\tFüllen\tFill\tunchanged\n原译文\tAlt\tKeep\t\n相同\tGleich\tSame\t\n原文\tAlt\tBlank\t", export: true);

        Assert.Equal((1, 2, 2, false), file.Compare(before, after));
        Assert.Equal((0, 0, 2, false), file.Compare(after, after));
        Assert.Equal((0, 0, 2, true), file.Compare(before, before));
    }

    [Fact]
    public void DuplicateFileReferences_DoNotDoubleCountEntriesOrCells()
    {
        TranslationTable file = Parse("en_US\tzh_CN\nNew\t新\nNew\t新\nFill\t填\nFill\t填\nKeep\t新\nKeep\t新");
        TranslationTable before = Parse("en_US\tzh_CN\nFill\t\nKeep\t旧", export: true);
        TranslationTable after = Parse("en_US\tzh_CN\nNew\t新\nFill\t填\nKeep\t旧", export: true);

        Assert.Equal((1, 1, 1, false), file.Compare(before, after));
    }

    [Fact]
    public void Matching_UsesTheSelectedReferenceAndIgnoresEmptyExportReferences()
    {
        TranslationTable file = Parse("en_US\tde_DE\nMotor\tMotor", "de_DE");
        TranslationTable before = Parse("de_DE\ten_US\n\tMotor", export: true);
        TranslationTable after = Parse("en_US\tde_DE\nMotor\tMotor", export: true);

        Assert.Equal((1, 0, 0, false), file.Compare(before, after));
        Assert.Equal((0, 0, 0, true), file.Compare(after, before));
    }

    [Fact]
    public void ACellNotFilledWithTheFileText_IsNotCountedAsFilled()
    {
        TranslationTable file = Parse("en_US\tzh_CN\nMotor\t电机");
        TranslationTable before = Parse("en_US\tzh_CN\nMotor\t", export: true);
        TranslationTable after = Parse("en_US\tzh_CN\nMotor\t马达", export: true);

        Assert.Equal((0, 0, 0, false), file.Compare(before, after));
    }

    private static TranslationTable Parse(string text, string referenceLanguage = "en_US", bool export = false) =>
        TranslationTable.Parse([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)], referenceLanguage, export);

    private static BridgeError Invalid(Action action)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(action).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        return error;
    }
}
