using System.Collections.ObjectModel;

namespace TiaOpenness.Gui.ViewModels;

/// <summary>
/// Stands in for TIA's device tree node, for which Primer.xaml declares a template (<c>Ui.DeviceNodeTemplate</c>). The
/// Workbench has no device tree; the type exists so that the vendored theme compiles unchanged.
/// </summary>
public sealed class DeviceNode
{
    public string Label { get; init; } = string.Empty;

    public ObservableCollection<DeviceNode> Children { get; } = [];
}
