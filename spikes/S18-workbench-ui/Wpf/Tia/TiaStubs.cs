// Stand-ins for the TIA Workbench types that Primer.xaml and the copied helpers name but the approval card does not
// use. They keep the copied theme files unchanged; each one says what it replaces. Written for spike S18; FeatureTone,
// LogLevel and EnumIndexConverter repeat TIA's definitions: Copyright (c) 2026 bulaofen0036-coder. MIT License.
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TiaOpenness.Gui.Localization
{
    /// <summary>
    /// Replaces TIA's Strings.cs (the whole UI catalogue, about 55 KB). Only the keys the copied theme itself reads:
    /// accessible names of scroll bars and buttons inside its templates. The card's own text uses UiText.
    /// </summary>
    internal static class Strings
    {
        internal static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Access.Scroll"] = "Scroll",
            ["Access.ScrollUp"] = "Scroll up",
            ["Access.ScrollDown"] = "Scroll down",
            ["Access.ScrollLeft"] = "Scroll left",
            ["Access.ScrollRight"] = "Scroll right",
            ["Access.ScrollPosition"] = "Scroll position",
        };

        internal static readonly IReadOnlyDictionary<string, string> Chinese = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Access.Scroll"] = "滚动",
            ["Access.ScrollUp"] = "向上滚动",
            ["Access.ScrollDown"] = "向下滚动",
            ["Access.ScrollLeft"] = "向左滚动",
            ["Access.ScrollRight"] = "向右滚动",
            ["Access.ScrollPosition"] = "滚动位置",
        };
    }
}

namespace TiaOpenness.Gui.ViewModels
{
    /// <summary>Same values as TIA's FeaturePagesViewModel.cs; Primer's tone triggers compare Tag with them.</summary>
    public enum FeatureTone { Normal, Muted, Accent, Warning, Success, Unchecked }

    /// <summary>Replaces TIA's device tree node; Primer only declares a template for it.</summary>
    public sealed class DeviceNode
    {
        public string Label { get; init; } = "";

        public ObservableCollection<DeviceNode> Children { get; } = [];
    }
}

namespace TiaOpenness.Gui.Controls
{
    /// <summary>Same values as TIA's ResultPresentation.cs.</summary>
    public enum LogLevel { Default, Info, Warning, Error, Debug }

    /// <summary>Same as TIA's ResultPresentation.cs.</summary>
    public sealed class EnumIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => System.Convert.ToInt32(value, culture);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Enum.ToObject(targetType, value);
    }

    /// <summary>Replaces TIA's DisplayValueConverter, which formats TIA result types; here the value passes through.</summary>
    public sealed class DisplayValueConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is null || value == DependencyProperty.UnsetValue ? Binding.DoNothing : value;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => Convert(values[0], targetType, parameter, culture);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// Replaces TIA's AccessibleNameConverter, which names TIA's row types (calls, audit rows, blocks); here a row's
    /// accessible name is its text.
    /// </summary>
    public sealed class AccessibleNameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values.Length == 0 || values[0] == DependencyProperty.UnsetValue ? "" : values[0]?.ToString() ?? "";

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
