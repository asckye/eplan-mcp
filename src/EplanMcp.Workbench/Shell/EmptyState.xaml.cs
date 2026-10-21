using System.Windows;
using System.Windows.Controls;

namespace EplanMcp.Workbench.Shell;

/// <summary>The one empty-state component (workbench-ui ERR-05): a title and a sentence, both from the catalogue.</summary>
public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public EmptyState() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
