// Copied for spike S18 from the TIA Portal Openness MCP repository, src/Studio/Gui/Controls/BoolConverters.cs, unchanged below this header.
// Copyright (c) 2026 bulaofen0036-coder. MIT License.
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TiaOpenness.Gui.Controls;

/// <summary>
/// Shows an element when the bound flag is <c>false</c>.
///
/// Empty states are the common case: a placeholder is visible exactly when the list behind it is
/// not. Doing that with a converter keeps one flag on the view model instead of a second property
/// whose only job is to be the negation of the first, which is a thing that can get out of step.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed or Visibility.Hidden;
}
