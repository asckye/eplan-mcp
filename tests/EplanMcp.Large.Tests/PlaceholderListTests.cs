using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_placeholder_list on the fixture (docs/tools/eplan_placeholder_list.md, section 7): the sample project's two
/// placeholders as spike S29 read them (api-notes A33), the page filter, the detailed values written to the test output
/// for comparison with EPLAN's placeholder dialog (GetValue is used where S29 read get_Value), and no change to the files.
/// </summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlaceholderListTests(LiveEplan eplan)
{
    private const string Tm1 = "=TM1+A1&EFS1/1";
    private const string Tm2 = "=TM2+A1&EFS1/1";

    [Fact]
    public async Task TheWholeProject_HasTheTwoPlaceholdersS29Found()
    {
        JsonElement listed = await eplan.CallAsync("eplan_placeholder_list");

        Assert.Equal(2, listed.GetProperty("total").GetInt32());
        JsonElement[] placeholders = [.. listed.GetProperty("placeholders").EnumerateArray()];
        Assert.Equal([Tm1, Tm2], placeholders.Select(p => p.GetProperty("page").GetString()));
        Assert.All(placeholders, p =>
        {
            Assert.Equal(string.Empty, p.GetProperty("name").GetString());
            Assert.Equal(["V", "mA"], p.GetProperty("valueSets").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(["T1", "T2"], p.GetProperty("variables").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(2, p.GetProperty("assignedObjects").GetInt32());
            Assert.False(p.TryGetProperty("values", out _));
        });
    }

    [Fact]
    public async Task APage_HasOnlyItsPlaceholder_AndAnUnknownPageIsNotFound()
    {
        JsonElement listed = await eplan.CallAsync("eplan_placeholder_list", new() { ["page"] = Tm1 });
        JsonElement error = await eplan.ErrorAsync("eplan_placeholder_list", new() { ["page"] = "=TM9+A1&EFS1/1" });

        Assert.Equal((Tm1, 1), (listed.GetProperty("page").GetString(), listed.GetProperty("total").GetInt32()));
        Assert.Equal("PAGE_NOT_FOUND", error.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Detailed_GivesFourValuesAndTwoCurrentValues_AndLeavesTheProjectFilesUnchanged()
    {
        Dictionary<string, (long Size, DateTime Written)> before = NoSideEffectTests.ProjectFiles(eplan.ProjectPath, withUserData: true);

        JsonElement listed = await eplan.CallAsync("eplan_placeholder_list", new() { ["response_format"] = "detailed" });

        Dictionary<string, (long Size, DateTime Written)> after = NoSideEffectTests.ProjectFiles(eplan.ProjectPath, withUserData: true);
        foreach (JsonElement placeholder in listed.GetProperty("placeholders").EnumerateArray())
        {
            JsonElement[] values = [.. placeholder.GetProperty("values").EnumerateArray()];
            JsonElement[] current = [.. placeholder.GetProperty("currentValues").EnumerateArray()];
            Assert.Equal([("V", "T1"), ("V", "T2"), ("mA", "T1"), ("mA", "T2")], values.Select(v => (v.GetProperty("valueSet").GetString(), v.GetProperty("variable").GetString())));
            Assert.Contains(values, v => v.GetProperty("text").GetString() is { Length: > 0 });
            Assert.Equal(2, current.Length);
            TestContext.Current.TestOutputHelper?.WriteLine($"{placeholder.GetProperty("page")}: values {JsonSerializer.Serialize(values)}; current {JsonSerializer.Serialize(current)}");
        }

        string[] changed = [.. after.Where(f => !before.TryGetValue(f.Key, out var old) || old != f.Value).Select(f => f.Key).Concat(before.Keys.Except(after.Keys))];
        Assert.True(changed.Length == 0, "Changed by reading: " + string.Join(", ", changed));
    }
}
