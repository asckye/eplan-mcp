using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>eplan_macro_list and eplan_macro_get on the live system's macro directory (EPLAN's own sample macros, spike S10).</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class MacroTests(LiveEplan eplan)
{
    internal const string FeedbackControl = "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema";

    [Fact]
    public async Task MacroList_FindsTheSampleMacroByName()
    {
        JsonElement found = await eplan.CallAsync("eplan_macro_list", new() { ["name_contains"] = "contactor_feedback" });

        JsonElement macro = Assert.Single(found.GetProperty("macros").EnumerateArray(), m => m.GetProperty("path").GetString() == FeedbackControl);
        Assert.Equal("Contactor_Feedback_Control", macro.GetProperty("name").GetString());
        Assert.False(found.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task MacroList_ListsEveryWindowMacro_InOneCompleteScan()
    {
        JsonElement all = await eplan.CallAsync("eplan_macro_list", new() { ["limit"] = 1 });

        // The sample data had 695 window macros on 2026-10-20 (S10); more may be added, never fewer than the one above.
        Assert.InRange(all.GetProperty("total").GetInt32(), 1, 100_000);
        Assert.Null(all.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task MacroGet_ReadsTheSampleMacro_AsS10Did()
    {
        JsonElement macro = (await eplan.CallAsync("eplan_macro_get", new() { ["macro"] = FeedbackControl })).GetProperty("macro");

        JsonElement multiLine = Assert.Single(macro.GetProperty("representations").EnumerateArray());
        Assert.Equal(("MultiLine", 6), (multiLine.GetProperty("type").GetString(), multiLine.GetProperty("variants").GetArrayLength()));
        Assert.Equal((52.0, 268.0), (macro.GetProperty("size").GetProperty("width").GetDouble(), macro.GetProperty("size").GetProperty("height").GetDouble()));
        string[] visible = [.. macro.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("visibleTag").GetString() ?? string.Empty)];
        Assert.Contains("-QA1", visible);
        Assert.Contains("-WG6", visible);
        Assert.Equal(28, macro.GetProperty("objectCount").GetInt32());
    }
}
