namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryComposerTests
{
    [Fact]
    public void RegistryKeepsTheApprovedOrder() => Assert.Equal(
        ["Properties", "Methods", "StaticEntries", "Files", "Selection", "Settings", "Handles", "ArraysOut", "NativePreconditions", "Lifecycle", "HandleChains"],
        ApiTrySliceRegistry.Slices.Select(s => s.Name));

    [Fact]
    public void DuplicateOwnershipReportsSignatureAndBothSlicesBeforeRendering()
    {
        const string signature = "Eplan.EplApi.DataModel.Page.GetName() : System.String";
        bool rendered = false;
        IApiTrySlice Slice(string name) => new ApiTryAdapter(name, _ => new()
        {
            Members = [new(signature, ApiTryClaim.Own)],
            Templates = { [ApiTrySlot.Catalog] = (_, _) => { rendered = true; return "unexpected"; } },
        });
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new ApiTryComposer("", [Slice("Methods"), Slice("Handles")]));
        Assert.Equal($"api_try signature '{signature}' is owned by both slices 'Methods' and 'Handles'. Use Decorate for shared contributions.", exception.Message);
        Assert.False(rendered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecorationsAndExclusionsDoNotClaimOwnership(bool decorateFirst)
    {
        IApiTrySlice owner = new ApiTryAdapter("Methods", _ => new() { Members = [new("signature", ApiTryClaim.Own)] });
        IApiTrySlice decoration = new ApiTryAdapter("ArraysOut", _ => new() { Members = [new("signature", ApiTryClaim.Decorate)] });
        IApiTrySlice excluded = new ApiTryAdapter("StaticEntries", _ => new() { Members = [new("signature", ApiTryClaim.Own, "static_or_nonpublic")] });
        var composer = new ApiTryComposer("", decorateFirst ? [decoration, excluded, owner] : [owner, decoration, excluded]);
        Assert.Equal("", composer.Render(ApiTrySlot.Dispatch));
    }

    [Fact]
    public void ContributionsAndOutputSlotsAreComposedInRegistryOrder()
    {
        var calls = new List<string>();
        IApiTrySlice Slice(string name) => new ApiTryAdapter(name, _ =>
        {
            calls.Add(name);
            return new()
            {
                DispatchFragments = { [ApiTrySlot.Dispatch] = (_, _) => name },
                TableRows = { [ApiTrySlot.SettingsCatalog] = (_, _) => name + ":rows;" },
                Templates = { [ApiTrySlot.Catalog] = (c, original) => original + c.Render(ApiTrySlot.SettingsCatalog) },
            };
        });
        var composer = new ApiTryComposer("", [Slice("Properties"), Slice("Methods")]);
        Assert.Equal(["Properties", "Methods"], calls);
        Assert.Equal("PropertiesMethods", composer.Render(ApiTrySlot.Dispatch));
        Assert.Equal("Properties:rows;Methods:rows;", composer.Render(ApiTrySlot.SettingsCatalog));
        Assert.Equal("prefixProperties:rows;Methods:rows;prefixProperties:rows;Methods:rows;", composer.Render(ApiTrySlot.Catalog, "prefix"));
    }
}
