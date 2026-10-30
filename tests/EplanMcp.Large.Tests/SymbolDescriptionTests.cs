using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>Pending SYMB_DESC readback on IEC_symbol; prints contact variants for human selection.</summary>
[Trait("Size", "Large")]
public sealed class SymbolDescriptionTests(WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task Search_LargeWriteIecContacts_ReadsDescriptionsAcrossAllPages()
    {
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(host.Writable));
        bool describedNoContact = false;
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            JsonElement data = await host.CallAsync("eplan_symbol_search", new()
            {
                ["project"] = host.Writable,
                ["library"] = "IEC_symbol",
                ["kind"] = "function",
                ["limit"] = 200,
                ["cursor"] = cursor,
            });
            foreach (JsonElement symbol in data.GetProperty("symbols").EnumerateArray())
            {
                string? category = symbol.GetProperty("functionCategory").GetString();
                if (category is not ("NoContact" or "NcContact"))
                { continue; }
                string? description = symbol.TryGetProperty("description", out JsonElement text) ? text.GetString() : null;
                describedNoContact |= category == "NoContact" && !string.IsNullOrWhiteSpace(description);
                TestContext.Current.TestOutputHelper!.WriteLine($"name={symbol.GetProperty("name").GetString()} variant={symbol.GetProperty("variant").GetInt32()} category={category} description={description ?? "<empty>"}");
            }
            cursor = data.GetProperty("nextCursor").GetString();
            if (cursor is not null)
            { Assert.True(cursors.Add(cursor), "Paging must advance through the contact variants."); }
        } while (cursor is not null);

        Assert.True(describedNoContact, "At least one IEC_symbol NoContact must have a non-empty SYMB_DESC.");
    }
}
