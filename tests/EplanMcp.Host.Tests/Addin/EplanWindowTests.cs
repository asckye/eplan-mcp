using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>A window's title is EPLAN's text: named to the AI client only escaped and cut short.</summary>
[Trait("Size", "Small")]
public sealed class EplanWindowTests
{
    [Fact]
    public void ATitle_WithControlCharacters_IsEscaped()
    {
        var window = new EplanWindow(1, "#32770", "Save\nchanges?\u202E", Enabled: true, Owner: 0, []);

        Assert.Equal("'Save\\u000achanges?\\u202e' (#32770)", window.ToString());
    }

    [Fact]
    public void ALongTitle_IsCut()
    {
        var window = new EplanWindow(1, "#32770", new string('x', 200), Enabled: true, Owner: 0, []);

        Assert.Equal("'" + new string('x', EplanWindow.MaxTitleLength) + "…' (#32770)", window.ToString());
    }
}
