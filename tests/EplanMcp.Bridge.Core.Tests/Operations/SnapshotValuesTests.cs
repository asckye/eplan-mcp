using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>The normalisation of snapshot values (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.3).</summary>
[Trait("Size", "Small")]
public sealed class SnapshotValuesTests
{
    private static readonly IReadOnlyDictionary<int, SnapshotProperty> _registered = SnapshotCanonical.Registered(SnapshotProfiles.StandardName)!;

    [Theory]
    [InlineData("53/4/21/0", "#4/21/0")] // A36: property 2000 before and after reopening
    [InlineData("89/4/21/0", "#4/21/0")]
    [InlineData("ref 28/17/59641/0 and 28/17/59642/0", "ref #17/59641/0 and #17/59642/0")]
    [InlineData("1/2/3", "1/2/3")]
    [InlineData("1/2/3/4/5", "1/2/3/4/5")]
    [InlineData("2026/10/21", "2026/10/21")]
    public void Text_RuntimeIdentifiers_LoseTheirDatabaseNumber(string raw, string expected) =>
        Assert.Equal(expected, SnapshotValues.Text(raw));

    [Fact]
    public void Text_LineEndsAndUnicode_AreNormalised() =>
        Assert.Equal("a\nb\nc é", SnapshotValues.Text("a\r\nb\rc é"));

    [Fact]
    public void Languages_SplitsTheRawValue_SortedByCode_EmptyAndRepeatedDropped()
    {
        List<SnapshotText>? texts = SnapshotValues.Languages("zh_CN@电机;de_DE@Motor;en_US@;??_??@-M1;de_DE@Anders;");

        Assert.NotNull(texts);
        Assert.Equal([("??_??", "-M1"), ("de_DE", "Motor"), ("zh_CN", "电机")], texts.Select(t => (t.Language, t.Text)));
    }

    [Theory]
    [InlineData("Motor")]
    [InlineData("see de_DE@x")]
    public void Languages_AValueThatDoesNotStartWithACode_IsNull(string raw) => Assert.Null(SnapshotValues.Languages(raw));

    [Theory]
    [InlineData(159.179375, 93.103115046302008, "159.179,93.103")]
    [InlineData(0.0625, -0.0625, "0.063,-0.063")]
    [InlineData(-0.0004, 120.0, "0,120")]
    [InlineData(180.5, 4, "180.5,4")]
    public void Position_IsRoundedTo0001Millimetres_CultureInvariant(double x, double y, string expected) =>
        Assert.Equal(expected, SnapshotValues.Position(x, y));

    [Fact]
    public void Value_MultiLanguage_HasTexts_AndPlainOnesAValue()
    {
        SnapshotValue? description = SnapshotValues.Value(new SnapshotSourceValue(11011, null, "en_US@Power;zh_CN@电源;"), _registered);
        SnapshotValue? plc = SnapshotValues.Value(new SnapshotSourceValue(20400, null, "%QX2.0"), _registered);

        Assert.Equal(("PAGE_NOMINATIOMN", null, 2), (description!.Name, description.Value, description.Texts!.Count));
        Assert.Equal(("FUNC_PLCADDRESS", "%QX2.0", null), (plc!.Name, plc.Value, plc.Texts));
    }

    [Theory]
    [InlineData(11024, "1792643412")] // a hint
    [InlineData(3010, "EPL")] // PROPUSER_LAST_USERCODE: not registered
    [InlineData(2000, "53/4/21/0")] // PROPUSER_DBOBJECTID: not registered
    [InlineData(20011, "de_DE@;en_US@;")] // nothing in any language
    [InlineData(20027, "")]
    public void Value_HintsUnregisteredAndEmptyValues_AreLeftOut(int id, string raw) =>
        Assert.Null(SnapshotValues.Value(new SnapshotSourceValue(id, null, raw), _registered));

    [Fact]
    public void Hint_JoinsDateAndTime_AndIsNullWithoutEither()
    {
        Assert.Equal("1792643412 36000", SnapshotValues.Hint("1792643412", "36000"));
        Assert.Equal("1792643412", SnapshotValues.Hint("1792643412", null));
        Assert.Null(SnapshotValues.Hint(null, string.Empty));
    }
}
