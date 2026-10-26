using System.Globalization;
using System.Text;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyValueValidationTests
{
    private const string At = "/changes/3";
    private const string ValuePath = At + "/value";

    private static PropertyWriteEntry Entry(string type = "text") => new()
    {
        EplanMajorVersion = 2026,
        ObjectType = "Function",
        PropertyId = 20011,
        PropertyName = "FUNC_TEXT",
        ApplicableCategory = "Terminal",
        Definition = new()
        {
            ValueType = type,
            MaximumLength = 500,
            EmptyAllowed = false,
            IsIndexed = false,
            Unit = "",
            LanguageMode = type == "multi_language" ? "explicit_merge_preserve" : "none",
            Format = type == "datetime" ? "O" : "literal",
            LowerBound = type switch
            {
                "integer" => new("integer", integerValue: long.MinValue),
                "number" => new("number", number: -double.MaxValue),
                _ => null,
            },
            UpperBound = type switch
            {
                "integer" => new("integer", integerValue: long.MaxValue),
                "number" => new("number", number: double.MaxValue),
                _ => null,
            },
            EnumValues = type == "enum" ? [new("enum", text: "First"), new("enum", text: "Second")] : null,
        },
    };

    private static JToken Json(string json) => BridgeJson.Parse(Encoding.UTF8.GetBytes(json));

    [Theory]
    [InlineData("text", "\"literal;@text\"")]
    [InlineData("multi_language", "\"de_DE@literal;en_US@literal;\"")]
    [InlineData("boolean", "true")]
    [InlineData("boolean", "false")]
    [InlineData("integer", "-9223372036854775808")]
    [InlineData("integer", "9223372036854775807")]
    [InlineData("number", "-1.5")]
    [InlineData("number", "0")]
    [InlineData("number", "1.7976931348623157E+308")]
    [InlineData("datetime", "\"2026-10-25T12:34:56.1234567+08:00\"")]
    [InlineData("enum", "\"Second\"")]
    public void Value_MatchingJsonPrimitive_ReturnsTypedPayload(string type, string json)
    {
        var violations = new List<PropertyViolation>();
        PropertyTypedValue? result = PropertyValueValidation.Value(Json(json), Entry(type), ValuePath, violations);
        Assert.Empty(violations);
        Assert.Equal(type, result!.ValueType);
    }

    public static TheoryData<string, string> Mismatches()
    {
        var cases = new TheoryData<string, string>();
        string[] types = ["text", "multi_language", "boolean", "integer", "number", "datetime", "enum"];
        string[] primitives = ["\"text\"", "true", "1", "1.5"];
        foreach (string type in types)
        {
            foreach (string json in new[] { "null", "{}", "[]", "{\"reset\":true}", "{\"delete\":true}" })
            {
                cases.Add(type, json);
            }

            for (int i = 0; i < primitives.Length; i++)
            {
                bool matches = type switch
                {
                    "text" or "multi_language" => i == 0,
                    "boolean" => i == 1,
                    "integer" => i == 2,
                    "number" => i is 2 or 3,
                    "enum" => i is 0 or 2,
                    _ => false,
                };
                if (!matches)
                {
                    cases.Add(type, primitives[i]);
                }
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Mismatches))]
    [InlineData("integer", "9223372036854775808")]
    [InlineData("integer", "-9223372036854775809")]
    [InlineData("integer", "1.0")]
    [InlineData("integer", "\"1\"")]
    [InlineData("number", "\"1.5\"")]
    [InlineData("boolean", "\"true\"")]
    [InlineData("opaque", "\"x\"")]
    [InlineData("reset", "\"\"")]
    [InlineData("TEXT", "\"x\"")]
    public void Value_WrongTypeNullResetOrOverflow_ReportsValuePointer(string type, string json)
    {
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyValueValidation.Value(Json(json), Entry(type), ValuePath, violations));
        Assert.Contains(violations, v => v.Path == ValuePath && v.Reason == ErrorReason.InvalidArgument);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Value_NonfiniteJsonNumber_IsRejectedBeforeDtoConstruction(double value)
    {
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyValueValidation.Value(new JValue(value), Entry("number"), ValuePath, violations));
        Assert.Equal(ValuePath, Assert.Single(violations).Path);
    }

    [Fact]
    public void Value_MissingOrOverflowingNumber_IsRejected()
    {
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyValueValidation.Value(null, Entry(), ValuePath, violations));
        Assert.Null(PropertyValueValidation.Value(Json("1e400"), Entry("number"), "/changes/4/value", violations));
        Assert.Equal(new[] { ValuePath, "/changes/4/value" }, violations.Select(v => v.Path));
    }

    [Theory]
    [InlineData("text", 500, 500, true)]
    [InlineData("text", 501, 500, false)]
    [InlineData("text", 501, 900, false)]
    [InlineData("text", 4, 4, true)]
    [InlineData("text", 5, 4, false)]
    [InlineData("multi_language", 500, 500, true)]
    [InlineData("multi_language", 501, 500, false)]
    public void Value_TextLengths_UsesStricterProductAndEntryLimit(string type, int count, int maximum, bool valid)
    {
        PropertyWriteEntry entry = Entry(type);
        entry.Definition.MaximumLength = maximum;
        var violations = new List<PropertyViolation>();
        PropertyTypedValue? result = PropertyValueValidation.Value(new JValue(new string('字', count)), entry, ValuePath, violations);
        Assert.Equal(valid, result is not null);
        Assert.Equal(valid ? 0 : 1, violations.Count);
        Assert.All(violations, v => Assert.Equal(ValuePath, v.Path));
    }

    [Theory]
    [InlineData("text", true, true)]
    [InlineData("text", false, false)]
    [InlineData("text", null, false)]
    [InlineData("multi_language", true, true)]
    [InlineData("multi_language", false, false)]
    public void Value_EmptyText_RequiresExplicitPermission(string type, bool? allowed, bool valid)
    {
        PropertyWriteEntry entry = Entry(type);
        entry.Definition.EmptyAllowed = allowed;
        var violations = new List<PropertyViolation>();
        PropertyTypedValue? result = PropertyValueValidation.Value(new JValue(""), entry, ValuePath, violations);
        Assert.Equal(valid, result is not null);
        Assert.All(violations, v => Assert.Equal(ValuePath, v.Path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void Value_UnknownOrInvalidTextLimit_DeniesCandidate(int? maximum)
    {
        PropertyWriteEntry entry = Entry();
        entry.Definition.MaximumLength = maximum;
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyValueValidation.Value(new JValue("x"), entry, ValuePath, violations));
        Assert.Contains(violations, v => v.Path == ValuePath && v.Reason == ErrorReason.PolicyDenied);
    }

    [Theory]
    [InlineData(9007199254740992, false)]
    [InlineData(9007199254740993, true)]
    [InlineData(9007199254740994, true)]
    [InlineData(9007199254740995, false)]
    public void Value_IntegerBounds_AreInclusiveWithoutDoubleRounding(long inputValue, bool valid)
    {
        PropertyWriteEntry entry = Entry("integer");
        entry.Definition.LowerBound = new("integer", integerValue: 9007199254740993);
        entry.Definition.UpperBound = new("integer", integerValue: 9007199254740994);
        var violations = new List<PropertyViolation>();
        PropertyTypedValue? result = PropertyValueValidation.Value(new JValue(inputValue), entry, ValuePath, violations);
        Assert.Equal(valid, result is not null);
        if (valid)
        {
            Assert.Equal(inputValue, result!.IntegerValue);
        }

        Assert.All(violations, v => Assert.Equal(ValuePath, v.Path));
    }

    [Theory]
    [InlineData(-1.5, false)]
    [InlineData(-1, true)]
    [InlineData(1.5, true)]
    [InlineData(2, false)]
    public void Value_NumberBounds_AreInclusive(double number, bool valid)
    {
        PropertyWriteEntry entry = Entry("number");
        entry.Definition.LowerBound = new("number", number: -1);
        entry.Definition.UpperBound = new("number", number: 1.5);
        var violations = new List<PropertyViolation>();
        Assert.Equal(valid, PropertyValueValidation.Value(new JValue(number), entry, ValuePath, violations) is not null);
        Assert.All(violations, v => Assert.Equal(ValuePath, v.Path));
    }

    [Theory]
    [InlineData("missing_lower")]
    [InlineData("missing_upper")]
    [InlineData("wrong_type")]
    [InlineData("inverted")]
    public void Value_UnverifiedNumericBounds_DeniesCandidate(string problem)
    {
        PropertyWriteEntry entry = Entry("integer");
        switch (problem)
        {
            case "missing_lower":
                entry.Definition.LowerBound = null;
                break;
            case "missing_upper":
                entry.Definition.UpperBound = null;
                break;
            case "wrong_type":
                entry.Definition.LowerBound = new("number", number: 0);
                break;
            case "inverted":
                entry.Definition.LowerBound = new("integer", integerValue: 2);
                entry.Definition.UpperBound = new("integer", integerValue: 1);
                break;
        }

        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyValueValidation.Value(new JValue(1), entry, ValuePath, violations));
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Single(violations).Reason);
    }

    [Theory]
    [InlineData("\"First\"", true)]
    [InlineData("\"first\"", false)]
    [InlineData("\"Unknown\"", false)]
    [InlineData("1", false)]
    public void Value_EnumNames_AreExactAndDoNotAcceptNumbers(string json, bool valid)
    {
        var violations = new List<PropertyViolation>();
        Assert.Equal(valid, PropertyValueValidation.Value(Json(json), Entry("enum"), ValuePath, violations) is not null);
        Assert.All(violations, v => Assert.Equal(ValuePath, v.Path));
    }

    [Theory]
    [InlineData("-1", true)]
    [InlineData("9223372036854775807", true)]
    [InlineData("1", false)]
    [InlineData("\"-1\"", false)]
    [InlineData("-1.0", false)]
    public void Value_EnumNumbers_AreExactAndDoNotAcceptNamesOrFloatingPoint(string json, bool valid)
    {
        PropertyWriteEntry entry = Entry("enum");
        entry.Definition.EnumValues = [new("enum", integerValue: -1), new("enum", integerValue: long.MaxValue)];
        var violations = new List<PropertyViolation>();
        Assert.Equal(valid, PropertyValueValidation.Value(Json(json), entry, ValuePath, violations) is not null);
        Assert.All(violations, v => Assert.Equal(ValuePath, v.Path));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("mixed")]
    [InlineData("wrong_type")]
    public void Value_UnverifiedOrMixedEnumDefinition_DeniesCandidate(string problem)
    {
        PropertyWriteEntry entry = Entry("enum");
        entry.Definition.EnumValues = problem switch
        {
            "missing" => null,
            "empty" => [],
            "mixed" => [new("enum", text: "First"), new("enum", integerValue: 1)],
            _ => [new("text", text: "First")],
        };
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyValueValidation.Value(new JValue("First"), entry, ValuePath, violations));
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Single(violations).Reason);
    }

    [Theory]
    [InlineData("2026-10-25T12:34:56.1234567+08:00", true)]
    [InlineData("2026-10-25T12:34:56.1234567-07:00", true)]
    [InlineData("2026-10-25T12:34:56.1234567", false)]
    [InlineData("2026-10-25T12:34:56", false)]
    [InlineData("2026-02-30T12:34:56.1234567+08:00", false)]
    [InlineData("25.10.2026", false)]
    public void Value_DateTime_RequiresPublishedFormatAndExplicitOffset(string text, bool valid)
    {
        var violations = new List<PropertyViolation>();
        PropertyTypedValue? result = PropertyValueValidation.Value(new JValue(text), Entry("datetime"), ValuePath, violations);
        Assert.Equal(valid, result is not null);
        if (valid)
        {
            Assert.Equal(text, result!.DateTime!.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        Assert.All(violations, v => Assert.Equal(ValuePath, v.Path));
    }

    [Theory]
    [InlineData("-4", false)]
    [InlineData("-3", true)]
    [InlineData("7", true)]
    [InlineData("8", false)]
    [InlineData("7.0", false)]
    [InlineData("\"7\"", false)]
    [InlineData("null", false)]
    [InlineData("9999999999999999999999", false)]
    public void Qualifiers_Index_RequiresExplicitIntegerInInclusiveVerifiedRange(string json, bool valid)
    {
        PropertyWriteEntry entry = Entry();
        entry.Definition.IsIndexed = true;
        entry.Definition.MinimumIndex = -3;
        entry.Definition.MaximumIndex = 7;
        var violations = new List<PropertyViolation>();
        PropertyValueValidation.Qualifiers(Json(json), null, "", entry, At, violations);
        Assert.Equal(valid ? 0 : 1, violations.Count);
        Assert.All(violations, v => Assert.Equal(At + "/property/index", v.Path));
    }

    [Fact]
    public void Qualifiers_MissingIndexedOrUnexpectedScalarIndex_IsRejected()
    {
        PropertyWriteEntry indexed = Entry();
        indexed.Definition.IsIndexed = true;
        indexed.Definition.MinimumIndex = 2;
        indexed.Definition.MaximumIndex = 5;
        var violations = new List<PropertyViolation>();
        PropertyValueValidation.Qualifiers(null, null, "", indexed, At, violations);
        PropertyValueValidation.Qualifiers(new JValue(0), null, "", Entry(), "/changes/4", violations);
        PropertyValueValidation.Qualifiers(JValue.CreateNull(), null, "", Entry(), "/changes/5", violations);
        Assert.Equal(new[] { At + "/property/index", "/changes/4/property/index", "/changes/5/property/index" }, violations.Select(v => v.Path));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing_minimum")]
    [InlineData("missing_maximum")]
    [InlineData("inverted")]
    public void Qualifiers_UnknownOrInvalidIndexDefinition_DeniesCandidate(string problem)
    {
        PropertyWriteEntry entry = Entry();
        entry.Definition.IsIndexed = problem == "unknown" ? null : true;
        entry.Definition.MinimumIndex = problem == "missing_minimum" ? null : 2;
        entry.Definition.MaximumIndex = problem switch
        {
            "missing_maximum" => null,
            "inverted" => 1,
            _ => 5,
        };
        var violations = new List<PropertyViolation>();
        PropertyValueValidation.Qualifiers(new JValue(3), null, "", entry, At, violations);
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Single(violations).Reason);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("mm", "mm", true)]
    [InlineData("mm", "cm", false)]
    [InlineData("mm", "MM", false)]
    [InlineData("mm", "", false)]
    [InlineData("", "mm", false)]
    [InlineData(null, "", false)]
    [InlineData("mm", null, false)]
    public void Qualifiers_Unit_RequiresKnownExactStorageUnitWithoutConversion(string? published, string? actual, bool valid)
    {
        PropertyWriteEntry entry = Entry();
        entry.Definition.Unit = published;
        var violations = new List<PropertyViolation>();
        PropertyValueValidation.Qualifiers(null, null, actual, entry, At, violations);
        Assert.Equal(valid ? 0 : 1, violations.Count);
        Assert.All(violations, v => Assert.Equal(ValuePath, v.Path));
        if (published is null || actual is null)
        {
            Assert.Equal(ErrorReason.PolicyDenied, Assert.Single(violations).Reason);
        }
    }

    [Theory]
    [InlineData("\"zh_CN\"", true)]
    [InlineData("\"en_US\"", true)]
    [InlineData("\"de_DE\"", true)]
    [InlineData("\"??_??\"", false)]
    [InlineData("\"en-US\"", false)]
    [InlineData("\"EN_us\"", false)]
    [InlineData("\"en_US;de_DE\"", false)]
    [InlineData("\"en_US\n\"", false)]
    [InlineData("\"\"", false)]
    [InlineData("null", false)]
    [InlineData("1", false)]
    public void Qualifiers_MultiLanguage_RequiresSingleExplicitIsoCode(string json, bool valid)
    {
        var violations = new List<PropertyViolation>();
        PropertyValueValidation.Qualifiers(null, Json(json), "", Entry("multi_language"), At, violations);
        Assert.Equal(valid ? 0 : 1, violations.Count);
        Assert.All(violations, v => Assert.Equal(At + "/language", v.Path));
    }

    [Fact]
    public void Qualifiers_MissingMultiLanguageOrUnexpectedScalarLanguage_IsRejected()
    {
        var violations = new List<PropertyViolation>();
        PropertyValueValidation.Qualifiers(null, null, "", Entry("multi_language"), At, violations);
        PropertyValueValidation.Qualifiers(null, new JValue("en_US"), "", Entry(), "/changes/4", violations);
        PropertyValueValidation.Qualifiers(null, JValue.CreateNull(), "", Entry(), "/changes/5", violations);
        Assert.Equal(new[] { At + "/language", "/changes/4/language", "/changes/5/language" }, violations.Select(v => v.Path));
    }

    [Theory]
    [InlineData("text", null)]
    [InlineData("text", "explicit_merge_preserve")]
    [InlineData("multi_language", "none")]
    [InlineData("multi_language", "replace_all")]
    public void Qualifiers_UnknownOrMismatchedLanguageMode_DeniesCandidate(string type, string? mode)
    {
        PropertyWriteEntry entry = Entry(type);
        entry.Definition.LanguageMode = mode;
        var violations = new List<PropertyViolation>();
        PropertyValueValidation.Qualifiers(null, null, "", entry, At, violations);
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Single(violations).Reason);
    }

    [Fact]
    public void Validation_IndependentErrors_AccumulatesPointersAndNeverIncludesInputValues()
    {
        PropertyWriteEntry entry = Entry();
        entry.Definition.MaximumLength = 0;
        entry.Definition.IsIndexed = true;
        entry.Definition.MinimumIndex = 2;
        entry.Definition.MaximumIndex = 5;
        entry.Definition.Unit = "mm";
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyValueValidation.Value(new JValue("private text"), entry, ValuePath, violations));
        PropertyValueValidation.Qualifiers(new JValue(0), new JValue("en_US"), "cm", entry, At, violations);
        Assert.Null(PropertyValueValidation.ShippedEntry(2026, "Function", 20011, "Terminal", At + "/property", violations));
        Assert.Equal(new[] { ValuePath, At + "/property/index", ValuePath, At + "/language", At + "/property" }, violations.Select(v => v.Path));
        Assert.DoesNotContain(violations, v => v.Message.Contains("private text", StringComparison.Ordinal));
        Assert.Equal(ErrorReason.PolicyDenied, violations[^1].Reason);
    }

    [Fact]
    public void Value_ValidInputWithEarlierViolations_StillReturnsItsOwnTypedValue()
    {
        var violations = new List<PropertyViolation> { new() { Path = "/earlier" } };
        Assert.NotNull(PropertyValueValidation.Value(new JValue("x"), Entry(), ValuePath, violations));
        Assert.Single(violations);
    }

    [Theory]
    [InlineData(2026, "Function", 20011, "Terminal")]
    [InlineData(2025, "Function", 20011, "Terminal")]
    [InlineData(2027, "Function", 20011, "Terminal")]
    [InlineData(2026, "Page", 20011, "Terminal")]
    [InlineData(2026, "function", 20011, "Terminal")]
    [InlineData(2026, "Function", 20011, "*")]
    [InlineData(2026, "Project", 10013, "")]
    [InlineData(2026, "Connection", 31002, "")]
    [InlineData(2026, "Page", 3010, "")]
    [InlineData(2026, "StorableObject", 2000, "")]
    public void ShippedEntry_OutsideB1Keys_DeniesEveryOtherVersionTypeCategoryAndCandidate(int version, string type, int id, string category)
    {
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyValueValidation.ShippedEntry(version, type, id, category, At + "/property", violations));
        PropertyViolation error = Assert.Single(violations);
        Assert.Equal(ErrorReason.PolicyDenied, error.Reason);
        Assert.Equal(At + "/property", error.Path);
        Assert.Equal([(2026, "Function", 20011, ""), (2026, "Function", 20027, "")], PropertyWriteWhitelist.Entries.Keys.OrderBy(k => k.PropertyId));
    }

    [Fact]
    public void ShippedEntry_OnlyB1CatalogProperties_AreAllowed()
    {
        foreach (PropertyDictionaryEntry entry in PropertyCatalog.Entries.Where(e => e.PropertyId.HasValue))
        {
            var violations = new List<PropertyViolation>();
            PropertyWriteEntry? allowed = PropertyValueValidation.ShippedEntry(2026, entry.ObjectType, entry.PropertyId!.Value, "", "/property", violations);
            if (entry.ObjectType == "Function" && entry.PropertyId is 20011 or 20027)
            {
                Assert.NotNull(allowed);
                Assert.Empty(violations);
            }
            else
            {
                Assert.Null(allowed);
                Assert.Equal(ErrorReason.PolicyDenied, Assert.Single(violations).Reason);
            }
        }
    }
}
