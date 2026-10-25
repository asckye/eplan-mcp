using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyReadTextTests
{
    internal static FakeProperty Multilingual() => new()
    {
        Metadata = new PropertyDefinitionMetadata { ValueType = "multi_language", IsIndexed = false, IsInternal = false },
        Texts = { ["de_DE"] = "German text", ["en_US"] = "English text", ["zh_CN"] = "Chinese text" },
    };

    [Theory]
    [InlineData("de_DE", "de_DE", "German text", false)]
    [InlineData("fr_FR", null, null, false)]
    [InlineData(null, "zh_CN", "Chinese text", false)]
    public void Read_LanguageSelection_ReportsActualLanguageWithoutExplicitFallback(string? requested, string? actual, string? text, bool fallback)
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(Multilingual());
        PropertyReadArgs args = PropertyReadOperationTests.Args();
        args.Language = requested;

        PropertyReadRow row = Assert.Single(PropertyReadOperationTests.Read(adapter, args).Items);

        Assert.Equal(text is null ? "empty" : "value", row.Status);
        Assert.Equal(actual, row.Language);
        Assert.Equal(text, row.Value?.Text);
        Assert.Equal(fallback, row.Fallback);
        Assert.Null(row.Texts);
        Assert.Equal(0, adapter.StoredReads);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_OmittedLanguage_PrefersEnglishFallbackAndReportsIt()
    {
        FakeProperty property = Multilingual();
        property.Texts.Remove("zh_CN");
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);

        PropertyReadRow row = Assert.Single(PropertyReadOperationTests.Read(adapter).Items);

        Assert.Equal("en_US", row.Language);
        Assert.True(row.Fallback);
        Assert.Equal("English text", row.Value!.Text);
        Assert.Null(row.Display);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_LanguageIndependentText_IsPreservedInDetailedTranslations()
    {
        FakeProperty property = Multilingual();
        property.Texts.Add("??_??", "Independent text");
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);
        PropertyReadArgs args = PropertyReadOperationTests.Args();
        args.Language = "fr_FR";
        args.Detailed = true;

        PropertyReadRow row = Assert.Single(PropertyReadOperationTests.Read(adapter, args).Items);

        Assert.Equal("??_??", row.Language);
        Assert.False(row.Fallback);
        Assert.Equal("Independent text", row.Value!.Text);
        Assert.Equal("Independent text", row.Display);
        Assert.Equal(property.Texts.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => (t.Key, t.Value)), row.Texts!.Select(t => (t.Language, t.Text)));
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_Concise_DoesNotReadUnselectedTranslationsOrDisplay()
    {
        FakeProperty property = Multilingual();
        property.Texts["en_US"] = "someone@example.invalid";
        property.Display = @"C:\private\drawing";
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);
        PropertyReadArgs args = PropertyReadOperationTests.Args();
        args.Language = "zh_CN";

        PropertyReadRow row = Assert.Single(PropertyReadOperationTests.Read(adapter, args).Items);

        Assert.Equal("value", row.Status);
        Assert.Equal(1, adapter.Events.Count(e => e == "text"));
        Assert.DoesNotContain("display", adapter.Events);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData(@"C:\private\drawing")]
    [InlineData("c:/private/drawing")]
    [InlineData(@"\\server\private\drawing")]
    [InlineData("/home/private/drawing")]
    [InlineData("someone@example.invalid")]
    [InlineData("someone@exa\u202emple.invalid")]
    [InlineData("+1 (555) 010-1234")]
    [InlineData("mailto:someone")]
    [InlineData("tel:555")]
    [InlineData("file:///private")]
    [InlineData("https://example.invalid/data")]
    public void Read_SensitiveFreeText_RefusesWholeRow(string text)
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(new FakeProperty { Stored = text });

        PropertyReadRow row = Assert.Single(PropertyReadOperationTests.Read(adapter).Items);

        AssertRefused(row);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("display")]
    [InlineData("translation")]
    [InlineData("unit")]
    [InlineData("format")]
    public void Read_SensitiveDetailedView_CannotBypassRefusal(string field)
    {
        FakeProperty property = field == "display" ? new FakeProperty() : Multilingual();
        const string Sensitive = "someone@example.invalid";
        switch (field)
        {
            case "display":
                property.Display = Sensitive;
                break;
            case "translation":
                property.Texts["de_DE"] = Sensitive;
                break;
            case "unit":
                property.Metadata.Unit = Sensitive;
                break;
            case "format":
                property.Metadata.Format = Sensitive;
                break;
        }

        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);
        PropertyReadArgs args = PropertyReadOperationTests.Args();
        args.Detailed = true;

        AssertRefused(Assert.Single(PropertyReadOperationTests.Read(adapter, args).Items));

        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_SensitiveTextAfter500Characters_IsDetectedBeforeTruncation()
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(new FakeProperty { Stored = new string('x', 501) + " someone@example.invalid" });

        AssertRefused(Assert.Single(PropertyReadOperationTests.Read(adapter).Items));

        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_ControlAndBidiCharacters_AreRemovedBeforeTruncationWithOriginalLengthMarker()
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(new FakeProperty { Stored = "\0\r\u202e" + new string('x', 501) + "\n\t" });

        PropertyReadRow row = Assert.Single(PropertyReadOperationTests.Read(adapter).Items);

        Assert.Equal(new string('x', 500) + " … [truncated, 503 characters in total]", row.Value!.Text);
        Assert.Equal(503, row.OriginalLength);
        adapter.AssertDisposed();
    }

    [Fact]
    public void CleanText_Truncation_DoesNotSplitSurrogatePair()
    {
        string input = new string('x', 499) + "\U0001f600";

        string? clean = PropertyReadText.CleanText(input, out int length);

        Assert.Equal(new string('x', 499) + " … [truncated, 501 characters in total]", clean);
        Assert.Equal(501, length);
    }

    [Theory]
    [InlineData("en_US", "English text", "value")]
    [InlineData("fr_FR", null, "empty")]
    public void Read_DetailedMultilingualDisplay_UsesOnlyTheSelectedLanguage(string language, string? expected, string status)
    {
        FakeProperty property = Multilingual();
        property.Display = "de_DE@German text;en_US@English text;";
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);
        PropertyReadArgs args = PropertyReadOperationTests.Args();
        args.Language = language;
        args.Detailed = true;

        PropertyReadRow row = Assert.Single(PropertyReadOperationTests.Read(adapter, args).Items);

        Assert.Equal(expected, row.Display);
        Assert.Equal(status, row.Status);
        Assert.False(row.Fallback);
        Assert.DoesNotContain("display", adapter.Events);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_SensitiveOpaqueDisplay_IsRefusedWithoutReadingStorage()
    {
        var property = new FakeProperty { Display = @"\\private\drawing" };
        property.Metadata.ValueType = "opaque";
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);

        AssertRefused(Assert.Single(PropertyReadOperationTests.Read(adapter).Items));

        Assert.Equal(0, adapter.StoredReads);
        adapter.AssertDisposed();
    }

    private static void AssertRefused(PropertyReadRow row)
    {
        Assert.Equal("read_refused", row.Status);
        Assert.Equal("sensitive_text", row.Reason);
        Assert.Null(row.Value);
        Assert.Null(row.Display);
        Assert.Null(row.Texts);
        Assert.Null(row.OriginalLength);
        Assert.Null(row.Unit);
        Assert.Null(row.Format);
    }
}
