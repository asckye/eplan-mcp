using System.Globalization;
using System.Windows.Data;
using EplanMcp.Workbench.Shell;

namespace TiaOpenness.Gui.Controls;

/// <summary>
/// Stands in for TIA's AccessibleNameConverter (<c>Primer.AccessibleName</c>), which names the rows of Primer's lists
/// after TIA's row types. Here a row names itself through <see cref="IAccessibleItem"/> (CTL-05), and nothing gets a
/// name from its CLR type; the second value, the language, only makes the binding update when the language changes.
/// </summary>
public sealed class AccessibleNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.FirstOrDefault() switch
    {
        IAccessibleItem item => item.AccessibleName,
        string text => text.Replace("_", string.Empty, StringComparison.Ordinal),
        _ => string.Empty,
    };

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
