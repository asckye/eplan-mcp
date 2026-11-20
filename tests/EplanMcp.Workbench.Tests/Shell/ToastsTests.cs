using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// Toasts (the 2026-11-20 design, section 5.3): a message in the window's corner that takes no input, is heard by screen
/// readers, never stacks past four, and goes away by itself.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ToastsTests(WpfContext wpf)
{
    [Fact]
    public void AToast_ShowsInTheCorner_TakesNoInput_AndIsAnnounced()
    {
        wpf.Run(() => With(window =>
        {
            Toasts.Show("saved", ToastTone.Success);
            WpfContext.Drain();

            var layer = (Panel)window.FindName("ToastLayer");
            var announcer = (TextBlock)window.FindName("ToastAnnouncer");
            Border toast = Assert.IsType<Border>(Assert.Single(layer.Children.Cast<UIElement>()));
            Assert.Contains("saved", Descendants(toast).OfType<TextBlock>().Select(t => t.Text));
            Assert.False(layer.IsHitTestVisible);
            Assert.Equal((HorizontalAlignment.Right, VerticalAlignment.Bottom), (layer.HorizontalAlignment, layer.VerticalAlignment));
            Assert.Equal("saved", announcer.Text);

            // One size for every toast: a short and a long message are equally wide, the long one wraps.
            Toasts.Show(new string('x', 200), ToastTone.Error);
            WpfContext.Drain();
            window.UpdateLayout();
            var shown = layer.Children.Cast<FrameworkElement>().ToList();
            Assert.Equal(shown[0].ActualWidth, shown[1].ActualWidth);
            Assert.True(shown[1].ActualHeight > shown[0].ActualHeight);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(announcer));
        }));
    }

    [Fact]
    public void AtMostFour_AreShown_TheOldestLeavingFirst()
    {
        wpf.Run(() => With(window =>
        {
            for (int i = 1; i <= 6; i++)
            {
                Toasts.Show($"t{i}", ToastTone.Info);
            }

            WpfContext.Drain();
            var layer = (Panel)window.FindName("ToastLayer");
            Assert.Equal(Toasts.MaxShown, layer.Children.Count);
            Assert.Contains("t6", Descendants(layer.Children[^1]).OfType<TextBlock>().Select(t => t.Text));
            Assert.DoesNotContain(layer.Children.Cast<UIElement>().SelectMany(Descendants).OfType<TextBlock>(), t => t.Text is "t1" or "t2");
        }));
    }

    [Fact]
    public void AToast_GoesAwayByItself()
    {
        TimeSpan before = Toasts.Lifetime;
        Toasts.Lifetime = TimeSpan.FromMilliseconds(50);
        try
        {
            wpf.Run(() => With(window =>
            {
                Toasts.Show("gone soon", ToastTone.Info);
                WpfContext.Drain();
                var layer = (Panel)window.FindName("ToastLayer");
                Assert.Single(layer.Children.Cast<UIElement>());

                DateTime until = DateTime.UtcNow.AddSeconds(5);
                while (layer.Children.Count > 0 && DateTime.UtcNow < until)
                {
                    System.Threading.Thread.Sleep(20);
                    WpfContext.Drain();
                }

                Assert.Empty(layer.Children);
            }));
        }
        finally
        {
            Toasts.Lifetime = before;
        }
    }

    [Fact]
    public void TurningApprovalOffOrOn_SaysSoInAToast()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory("eplanmcp-toasts-");
        Func<DependencyObject, bool> confirm = ApprovalPolicyUi.ConfirmOff;
        ApprovalPolicyUi.ConfirmOff = _ => true;
        var heard = new List<(string Text, ToastTone Tone)>();
        void Hear(string text, ToastTone tone) => heard.Add((text, tone));
        Toasts.Wanted += Hear;
        try
        {
            wpf.RunWithLanguage(AppLanguage.Chinese, () =>
            {
                var policy = new SafetyPolicyStore(Path.Combine(temp.FullName, "config.json"));
                policy.Load();

                Assert.True(ApprovalPolicyUi.Set(policy, require: false, from: new DependencyObject()));
                Assert.True(ApprovalPolicyUi.Set(policy, require: true, from: new DependencyObject()));
            });

            Assert.Equal(
                [("已关闭普通写入人工审批（破坏性与主数据仍强制确认）", ToastTone.Warning), ("已恢复普通写入人工审批", ToastTone.Success)],
                heard.Where(h => h.Text.Contains("人工审批", StringComparison.Ordinal)));
        }
        finally
        {
            Toasts.Wanted -= Hear;
            ApprovalPolicyUi.ConfirmOff = confirm;
            temp.Delete(recursive: true);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject element)
            {
                yield return element;
                foreach (DependencyObject below in Descendants(element))
                {
                    yield return below;
                }
            }
        }
    }
}
