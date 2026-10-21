using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EplanMcp.Workbench.Shell;

/// <summary>A page: its head (title and caption) above its content (workbench-ui 3.4, NAV-06).</summary>
public partial class PageView : UserControl, IWorkbenchPage
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(PageView), new PropertyMetadata(string.Empty));

    public PageView() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    /// <summary>The page title, where keyboard focus goes when a shortcut, the tray or a notification opens the page (NAV-07).</summary>
    public FrameworkElement? Heading => Template.FindName("PART_Title", this) as FrameworkElement;

    public void FocusHeading()
    {
        ApplyTemplate();
        if (Heading is { } heading)
        {
            Keyboard.Focus(heading);
        }
    }
}
