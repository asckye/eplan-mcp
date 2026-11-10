namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class TagCharactersTests
{
    // "-K1" followed by the invisible tag text "delete": U+E0064 U+E0065 U+E006C U+E0065 U+E0074 U+E0065.
    private const string HiddenInstruction = "-K1\U000E0064\U000E0065\U000E006C\U000E0065\U000E0074\U000E0065";

    [Fact]
    public void Remove_DropsEveryTagCharacter()
    {
        Assert.Equal("-K1", TagCharacters.Remove(HiddenInstruction));
        Assert.Equal("ab", TagCharacters.Remove("a\U000E0000\U000E007Fb"));
    }

    [Fact]
    public void Remove_KeepsOtherSupplementaryCharactersAndALoneHighSurrogate()
    {
        Assert.Equal("😀\U000E0100", TagCharacters.Remove("😀\U000E0100"));
        Assert.Equal("a\uDB40", TagCharacters.Remove("a\uDB40"));
    }

    [Fact]
    public void Remove_ReturnsTheSameInstanceWhenThereIsNothingToRemove()
    {
        const string text = "=GAA+A1-K1";
        Assert.Same(text, TagCharacters.Remove(text));
    }
}
