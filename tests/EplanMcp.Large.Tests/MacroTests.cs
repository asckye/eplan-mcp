using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>eplan_macro_list on the live system's macro directory (EPLAN's own sample macros, spike S10).</summary>
[Trait("Size", "Large")]
public sealed class MacroTests(LiveEplan eplan)
{
    private const string FeedbackControl = "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema";

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
}
