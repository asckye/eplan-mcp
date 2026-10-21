using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TiaOpenness.Gui.Controls;

/// <summary>
/// Stands in for TIA's DisplayValueConverter (<c>Primer.Value</c>), which formats TIA's result types. The Workbench
/// formats its own values before binding them, so the value passes through.
/// </summary>
public sealed class DisplayValueConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value == DependencyProperty.UnsetValue ? Binding.DoNothing : value;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => Convert(values[0], targetType, parameter, culture);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
