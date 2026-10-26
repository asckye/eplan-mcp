using System.Globalization;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LiteralTextReplaceTests
{
    [Theory]
    [InlineData("Motor Motor", "Motor", "Drive", "Drive Drive", 2)]
    [InlineData("Motor motor MOTOR", "Motor", "Drive", "Drive motor MOTOR", 1)]
    [InlineData("aaa", "aa", "b", "ba", 1)]
    [InlineData("aaaa", "aa", "b", "bb", 2)]
    [InlineData("a", "a", "aa", "aa", 1)]
    [InlineData("a.b a?b", "a.b", "$1\\", "$1\\ a?b", 1)]
    [InlineData("@en_US;@", "@", ";", ";en_US;;", 2)]
    [InlineData(" A \nA\t", " A ", "", "\nA\t", 1)]
    [InlineData("x\nx", "\n", "\t", "x\tx", 1)]
    [InlineData("电机😀电机", "电机", "驱动", "驱动😀驱动", 2)]
    [InlineData("😀😀", "😀", "", "", 2)]
    [InlineData("", "Motor", "Drive", "", 0)]
    [InlineData("Power", "Motor", "Drive", "Power", 0)]
    public void Match_LiteralAndNonOverlapping_ReplacesOriginalOccurrencesOnly(
        string before, string find, string replacement, string expected, int occurrences)
    {
        Assert.Equal((expected, occurrences), LiteralTextReplace.Apply(before, find, replacement, caseSensitive: true));
    }

    [Fact]
    public void Match_IgnoreCase_UsesOrdinalComparison()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            Assert.Equal(("Drive Drive Drive", 3), LiteralTextReplace.Apply("Motor motor MOTOR", "Motor", "Drive", caseSensitive: false));
            Assert.Equal(("x x", 2), LiteralTextReplace.Apply("I i", "i", "x", caseSensitive: false));
            Assert.Equal(("MOTOR", 1), LiteralTextReplace.Apply("Motor", "motor", "MOTOR", caseSensitive: false));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Match_EmptyFind_RefusesInsteadOfLooping()
    {
        Assert.Throws<ArgumentException>(() => LiteralTextReplace.Apply("Motor", string.Empty, "Drive", caseSensitive: true));
    }
}
