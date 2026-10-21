// Repeats TIA Portal Openness MCP's EnumIndexConverter (src/Studio/Gui/Controls/ResultPresentation.cs at commit
// c89b7c20), with the culture passed on (CA1305). Copyright (c) 2026 bulaofen0036-coder. MIT License, see
// THIRD-PARTY-NOTICES.md.
using System.Globalization;
using System.Windows.Data;

namespace TiaOpenness.Gui.Controls;

/// <summary>An enum as its index and back, for Primer.xaml's <c>Primer.EnumIndex</c>.</summary>
public sealed class EnumIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => System.Convert.ToInt32(value, culture);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Enum.ToObject(targetType, value);
}
