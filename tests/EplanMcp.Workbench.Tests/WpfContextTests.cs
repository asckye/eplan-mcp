using System.Windows;
using System.Windows.Controls;

namespace EplanMcp.Workbench.Tests;

/// <summary>
/// The shared WPF thread has the App's resources but not its startup: no window, and so none of what comes with it
/// (the data root's settings and logs, the single-instance mutex, the approval pipe, the tray).
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class WpfContextTests(WpfContext wpf)
{
    [Fact]
    public void TheApp_LoadsItsResources_ButDoesNotStart()
    {
        Assert.Equal(0, wpf.WindowsAtStart);
        Assert.Null(wpf.MainWindowAtStart);
        wpf.Run(() =>
        {
            Assert.IsType<App>(Application.Current);
            Assert.IsType<BooleanToVisibilityConverter>(Application.Current.FindResource("BoolToVisibility"));
        });
    }
}
