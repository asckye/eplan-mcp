using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("bindings.json in the pack store: which pack versions each project (or other target) is pinned to. Installing a newer version changes no binding.")]
public sealed record PackBindings
{
    public const int CurrentBindingsVersion = 1;

    public const string FileName = "bindings.json";

    [Description("The bindings format. A reader refuses a version it does not know.")]
    [Schema(Const = CurrentBindingsVersion)]
    public required int BindingsVersion { get; init; }

    [Description("Sorted by target.")]
    public IReadOnlyList<PackBinding> Bindings { get; init; } = [];
}
