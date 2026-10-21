using System.Windows;

namespace EplanMcp.Workbench.Shell;

/// <summary>A page the shell switches to.</summary>
internal interface IWorkbenchPage
{
    /// <summary>The element <see cref="FocusHeading"/> focuses.</summary>
    public FrameworkElement? Heading { get; }

    /// <summary>
    /// Moves keyboard focus to the page's heading: the page title, or on the approval queue the shown question's title
    /// (NAV-07, APR-03); never to a button.
    /// </summary>
    public void FocusHeading();
}
