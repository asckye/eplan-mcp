using System.Text.Json;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class AcceptanceListFileTests
{
    [Fact]
    public async Task DefaultAndExternalFile_ReadWithoutCopyingPrivateInputIntoRepository()
    {
        string folder = Directory.CreateTempSubdirectory("eplanmcp-acceptance-").FullName;
        try
        {
            string repository = Directory.CreateDirectory(Path.Combine(folder, "repo")).FullName;
            string fixture = Path.Combine(repository, "fixture.json"), external = Path.Combine(folder, "local.json");
            await File.WriteAllTextAsync(fixture, AcceptanceDrawingListTests.Fixture(), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(external, AcceptanceDrawingListTests.Fixture(), TestContext.Current.CancellationToken);
            JsonElement first = await AcceptanceListFile.LoadAsync(repository, fixture, null, TestContext.Current.CancellationToken);
            JsonElement second = await AcceptanceListFile.LoadAsync(repository, "not-used.json", external, TestContext.Current.CancellationToken);
            Assert.True(JsonElement.DeepEquals(first, second));
            Assert.Single(Directory.GetFiles(repository));
            await Assert.ThrowsAsync<InvalidOperationException>(() => AcceptanceListFile.LoadAsync(repository, fixture, fixture, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => AcceptanceListFile.LoadAsync(repository, fixture, @"\\server\share\private.json", TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => AcceptanceListFile.LoadAsync(repository, fixture, "relative.json", TestContext.Current.CancellationToken));
            await File.WriteAllTextAsync(external, new string(' ', AcceptanceDrawingList.MaxBytes + 1), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<JsonException>(() => AcceptanceListFile.LoadAsync(repository, fixture, external, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
