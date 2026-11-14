using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>Read-only coverage of every supported dictionary type on both prepared scratch fixtures; never creates objects.</summary>
[Trait("Size", "Large")]
public sealed class ObjectReadTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Theory]
    [InlineData("Page")]
    [InlineData("Function")]
    [InlineData("Connection")]
    [InlineData("Terminal")]
    [InlineData("TerminalStrip")]
    [InlineData("PLC")]
    [InlineData("Cable")]
    [InlineData("PlaceHolder")]
    [InlineData("LocationBox")]
    [InlineData("InterruptionPoint")]
    [InlineData("PotentialDefinition")]
    [InlineData("BlackBox")]
    [InlineData("PLCTerminal")]
    [InlineData("MacroBox")]
    public async Task SupportedType_HasNonemptyPropertiesOnFixtureOrLargeWrite_WithoutDesignChanges(string objectType)
    {
        bool foundObject = false;
        foreach (string project in new[] { eplan.ProjectPath, host.Writable })
        {
            Dictionary<string, string> before = LiveSnapshots.DesignFiles(project);
            try
            {
                JsonElement found = await eplan.CallAsync("eplan_object_find", new() { ["project"] = project, ["object_type"] = objectType, ["limit"] = 1 });
                Assert.Equal("supported", found.GetProperty("status").GetString());
                if (found.GetProperty("items").GetArrayLength() == 0)
                { continue; }
                foundObject = true;
                JsonElement obj = Assert.Single(found.GetProperty("items").EnumerateArray());
                var args = new Dictionary<string, object?> { ["project"] = project, ["object_type"] = objectType, ["locator"] = obj.GetProperty("locator"), ["language"] = "en_US", ["limit"] = 200 };
                bool hasValue = false;
                int pages = 0;
                do
                {
                    JsonElement read = await eplan.CallAsync("eplan_object_get", args);
                    Assert.Equal("supported", read.GetProperty("status").GetString());
                    foreach (JsonElement row in read.GetProperty("items").EnumerateArray())
                    {
                        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("property_name").GetString()));
                        hasValue |= row.GetProperty("status").GetString() == "value" && !string.IsNullOrWhiteSpace(row.GetProperty("value").ToString());
                    }
                    args["cursor"] = read.GetProperty("cursor").GetString();
                    Assert.InRange(++pages, 1, 100);
                } while (args["cursor"] is not null);
                Assert.True(hasValue, objectType + " must expose at least one nonempty property.");
            }
            finally
            {
                Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
            }
        }
        Assert.True(foundObject, objectType + " must exist in the fixture or the LargeWrite scratch copy.");
    }

    /// <summary>
    /// A found object must be readable whatever its category. Discovery under Terminal also returns terminals of
    /// the PLC category, which the reader describes as plcterminal; object_get used to build its property target
    /// from the requested type and refused every one of them (live 2026-11-14: all 31 terminals of a sample
    /// redraw copy are PLC terminals, so the one-object case above never saw it).
    /// </summary>
    [Theory]
    [InlineData("Terminal")]
    [InlineData("Function")]
    public async Task EveryFoundObject_IsReadable_WhateverItsCategory(string objectType)
    {
        foreach (string project in new[] { eplan.ProjectPath, host.Writable })
        {
            JsonElement found = await eplan.CallAsync("eplan_object_find", new() { ["project"] = project, ["object_type"] = objectType, ["limit"] = 20 });
            Assert.Equal("supported", found.GetProperty("status").GetString());
            foreach (JsonElement obj in found.GetProperty("items").EnumerateArray())
            {
                JsonElement read = await eplan.CallAsync("eplan_object_get", new Dictionary<string, object?>
                { ["project"] = project, ["object_type"] = objectType, ["locator"] = obj.GetProperty("locator"), ["limit"] = 200 });
                Assert.Equal("supported", read.GetProperty("status").GetString());
                Assert.NotEqual(0, read.GetProperty("items").GetArrayLength());
            }
        }
    }
}
