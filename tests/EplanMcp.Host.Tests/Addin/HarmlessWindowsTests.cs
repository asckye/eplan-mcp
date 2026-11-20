using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>Which EPLAN windows the closer may close for the engineer (ADR-0063, #664). Small: windows are data here.</summary>
[Trait("Size", "Small")]
public sealed class HarmlessWindowsTests
{
    [Fact]
    public void TheSystemMessagesWindow_IsOnTheList()
    {
        EplanWindow window = EplanTestWindows.SystemMessages;

        Assert.Same(HarmlessWindows.SystemMessages, HarmlessWindows.Find(window, [FakeEplan.MainFrame, window]));
    }

    [Fact]
    public void TheSystemMessagesWindow_IsRecognisedWhateverItsTitle()
    {
        EplanWindow window = EplanTestWindows.SystemMessages with { Title = "Systemmeldungen" };

        Assert.NotNull(HarmlessWindows.Find(window, [FakeEplan.MainFrame, window]));
    }

    [Fact]
    public void TheLicenceSelection_IsNotOnTheList()
    {
        EplanWindow window = EplanTestWindows.Licence;

        Assert.Null(HarmlessWindows.Find(window, [EplanTestWindows.Splash, window]));
    }

    [Fact]
    public void ASavePromptOwnedByTheMainFrame_IsNotOnTheList()
    {
        EplanWindow window = EplanTestWindows.SavePrompt;

        Assert.Null(HarmlessWindows.Find(window, [FakeEplan.MainFrame, window]));
    }

    [Fact]
    public void TheSystemMessagesLayout_OwnedByAnotherWindowThanTheMainFrame_IsNotOnTheList()
    {
        EplanWindow window = EplanTestWindows.SystemMessages with { Owner = EplanTestWindows.Splash.Handle };

        Assert.Null(HarmlessWindows.Find(window, [EplanTestWindows.Splash, FakeEplan.MainFrame, window]));
    }

    [Theory]
    [InlineData(1011)]
    [InlineData(1076)]
    [InlineData(1112)]
    [InlineData(2)]
    public void TheSystemMessagesLayout_WithoutOneOfItsControls_IsNotOnTheList(int missing)
    {
        EplanWindow window = EplanTestWindows.SystemMessages with { Controls = [.. EplanTestWindows.SystemMessages.Controls.Where(c => c.Id != missing)] };

        Assert.Null(HarmlessWindows.Find(window, [FakeEplan.MainFrame, window]));
    }

    [Fact]
    public void AGridOfAnotherClass_DoesNotPassForTheMessagesGrid()
    {
        EplanWindow window = EplanTestWindows.SystemMessages with
        {
            Controls = [.. EplanTestWindows.SystemMessages.Controls.Select(c => c.Id == 1011 ? c with { ClassName = "BCGPGridCtrlX" } : c)],
        };

        Assert.Null(HarmlessWindows.Find(window, [FakeEplan.MainFrame, window]));
    }

    [Fact]
    public void TheMainFrame_IsRecognisedAsSuch_AndNotAsHarmless()
    {
        Assert.True(HarmlessWindows.IsMainFrame(FakeEplan.MainFrame));
        Assert.False(HarmlessWindows.IsMainFrame(EplanTestWindows.SystemMessages));
        Assert.Null(HarmlessWindows.Find(FakeEplan.MainFrame, [FakeEplan.MainFrame]));
    }
}
