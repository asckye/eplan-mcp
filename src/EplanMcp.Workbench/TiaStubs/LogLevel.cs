// Repeats TIA Portal Openness MCP's LogLevel (src/Studio/Gui/Controls/ResultPresentation.cs at commit c89b7c20).
// Copyright (c) 2026 bulaofen0036-coder. MIT License, see THIRD-PARTY-NOTICES.md.
namespace TiaOpenness.Gui.Controls;

/// <summary>The level of a log row, which Primer.xaml's log styles compare; TIA's values, in TIA's order.</summary>
public enum LogLevel
{
    Default,
    Info,
    Warning,
    Error,
    Debug,
}
