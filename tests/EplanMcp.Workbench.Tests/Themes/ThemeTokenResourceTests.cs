using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace EplanMcp.Workbench.Tests.Themes;

[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ThemeTokenResourceTests(WpfContext wpf)
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public void EveryAlias_ResolvesToItsUnderlyingBrush_InTheEffectivePalette(string name)
    {
        wpf.Run(() =>
        {
            var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/EplanMcp.Workbench;component/Themes/Palette.{name}.xaml") };
            foreach (XElement alias in ThemeTokenTests.Aliases(name))
            {
                string key = (string)alias.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!;
                string source = Regex.Match((string)alias.Attribute("Color")!, @"StaticResource ([\w.]+)").Groups[1].Value;
                Assert.NotEmpty(source);
                Assert.Equal(Assert.IsType<SolidColorBrush>(palette[source]).Color, Assert.IsType<SolidColorBrush>(palette[key]).Color);
            }

            // System colour updates change the source brush in place, without replacing the palette.
            var sourceBrush = Assert.IsType<SolidColorBrush>(palette["Primer.bg"]);
            sourceBrush.SetCurrentValue(SolidColorBrush.ColorProperty, Colors.Purple);
            Assert.Equal(Colors.Purple, Assert.IsType<SolidColorBrush>(palette["Surface.Window"]).Color);
        });
    }

    [Fact]
    public void LayoutResources_HaveTheWpfTypesTheViewsNeed_AndStayOnTheGrid()
    {
        wpf.Run(() =>
        {
            XDocument layout = XDocument.Parse(SourceScan.Single("Themes/Layout.xaml").Text);
            foreach (XElement token in layout.Root!.Elements())
            {
                string key = (string)token.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!;
                object value = Application.Current.FindResource(key);
                double[] dimensions;
                if (token.Name.LocalName == "Thickness")
                {
                    Thickness thickness = Assert.IsType<Thickness>(value);
                    dimensions = [thickness.Left, thickness.Top, thickness.Right, thickness.Bottom];
                }
                else
                {
                    dimensions = [Assert.IsType<double>(value)];
                }

                Assert.All(dimensions, n => Assert.True(n % 4 == 0, $"{key}: {n.ToString(CultureInfo.InvariantCulture)}"));
            }

            var strong = new TextBlock { Style = (Style)Application.Current.FindResource("Ui.Text.BodyStrong") };
            Assert.Equal(FontWeights.SemiBold, strong.FontWeight);
            Assert.Equal(Application.Current.FindResource("Ui.FontSize.Body"), strong.FontSize);
        });
    }
}
