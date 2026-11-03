using System.Text.Json;
using EplanMcp.TestSupport;

namespace EplanMcp.Large.Tests;

// Read-only inventory, using the same LiveEplan fixture as SampleProjectInventorySpikeTests.
// No project open, navigation, generation, transaction or write tool is used here.
[Trait("Size", "Large")]
[Trait("Spike", "Inventory")]
public sealed class SmallProjectInventorySpikeTests(LiveEplan eplan)
{
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    [Fact]
    public async Task SelectedPages_AllReadableObjects_WritesTheInventory()
    {
        string project = eplan.ProjectPath;
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;
        string file = Path.Combine(folder, "small-project-inventory.json");
        var inventory = new SmallProjectInventory(eplan.ReadOrErrorAsync, TimeProvider.System,
            message => TestContext.Current.TestOutputHelper!.WriteLine(message));
        await inventory.RunAsync(project, async pages =>
        {
            // Cancellation during serialization/write must not replace the last complete JSON snapshot.
            string pending = file + ".tmp";
            await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(pages, _indented), TestContext.Current.CancellationToken);
            File.Move(pending, file, overwrite: true);
        }, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine("Inventory: " + file);
    }
}
