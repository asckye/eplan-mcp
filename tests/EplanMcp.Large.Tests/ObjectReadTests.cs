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
    public async Task SupportedType_HasNonemptyPropertiesOnFixtureAndLargeWrite_WithoutDesignChanges(string objectType)
    {
        foreach (string project in new[] { eplan.ProjectPath, host.Writable })
        {
            Dictionary<string, string> before = LiveSnapshots.DesignFiles(project);
            JsonElement found = await eplan.CallAsync("eplan_object_find", new() { ["project"] = project, ["object_type"] = objectType, ["limit"] = 1 });
            Assert.Equal("supported", found.GetProperty("status").GetString());
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
                    hasValue |= row.GetProperty("status").GetString() == "value";
                }
                args["cursor"] = read.GetProperty("cursor").GetString();
                Assert.InRange(++pages, 1, 100);
            } while (args["cursor"] is not null);
            Assert.True(hasValue, objectType + " must expose at least one nonempty property.");
            Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
        }
    }
}
