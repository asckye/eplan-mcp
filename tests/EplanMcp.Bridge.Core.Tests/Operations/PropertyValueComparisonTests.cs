using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyValueComparisonTests
{
    public static TheoryData<PropertyTypedValue?, PropertyTypedValue?, bool> Values => new()
    {
        { null, null, true },
        { null, new("text", text: ""), false },
        { new("text", text: "字;@"), new("text", text: "字;@"), true },
        { new("text", text: "text"), new("text", text: "Text"), false },
        { new("text", text: " text"), new("text", text: "text"), false },
        { new("text", text: "é"), new("text", text: "e\u0301"), false },
        { new("text", text: "x"), new("multi_language", text: "x"), false },
        { new("boolean", boolean: false), new("boolean", boolean: false), true },
        { new("boolean", boolean: false), new("boolean", boolean: true), false },
        { new("integer", integerValue: 9007199254740992), new("integer", integerValue: 9007199254740993), false },
        { new("integer", integerValue: long.MaxValue), new("integer", integerValue: long.MaxValue), true },
        { new("integer", integerValue: 1), new("number", number: 1), false },
        { new("number", number: 1.5), new("number", number: 1.5), true },
        { new("number", number: -0.0), new("number", number: 0.0), true },
        { new("number", number: 1.5), new("number", number: 1.5000000000000002), false },
        { new("enum", text: "First"), new("enum", integerValue: 1), false },
        { new("enum", integerValue: 1), new("enum", integerValue: 1), true },
        { new("datetime", dateTime: new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero)),
            new("datetime", dateTime: new DateTimeOffset(2026, 10, 26, 20, 0, 0, TimeSpan.FromHours(8))), true },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void Equal_TypedStorageValues_UsesExactPayloadWithUtcDateComparison(
        PropertyTypedValue? left, PropertyTypedValue? right, bool equal)
    {
        Assert.Equal(equal, PropertyValueComparison.Equal(left, right));
        Assert.Equal(equal, PropertyValueComparison.Equal(right, left));
    }
}
