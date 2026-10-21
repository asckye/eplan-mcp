// Repeats TIA Portal Openness MCP's FeatureTone (src/Studio/Gui/ViewModels/FeaturePagesViewModel.cs at commit c89b7c20).
// Copyright (c) 2026 bulaofen0036-coder. MIT License, see THIRD-PARTY-NOTICES.md.
namespace TiaOpenness.Gui.ViewModels;

/// <summary>The tone that Primer.xaml's status texts, chips and icons read from <c>Tag</c>; TIA's values, in TIA's order.</summary>
public enum FeatureTone
{
    Normal,
    Muted,
    Accent,
    Warning,
    Success,
    Unchecked,
}
