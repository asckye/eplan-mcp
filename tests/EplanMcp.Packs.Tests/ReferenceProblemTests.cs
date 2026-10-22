namespace EplanMcp.Packs.Tests;

/// <summary>Referenced resources checked below their packSources root (ADR-0024 section 6), in temporary folders.</summary>
[Trait("Size", "Medium")]
public sealed class ReferenceProblemTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private Dictionary<string, string> Roots => new() { ["company-refs"] = _folder.PathOf("share") };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reference_Unchanged_IsFine_AndAChangedFileIsNot()
    {
        _folder.Write("share/macros/Motor.ema", "<EplanPxfRoot />");
        PackResource resource = await ReferencedAsync("macros/Motor.ema");

        Assert.Null(await PackVerifier.ReferenceProblemAsync(resource, Roots, Cancel));
        _folder.Write("share/macros/Motor.ema", "<EplanPxfRoot Type=\"WindowMacro\" />");
        Assert.Contains("has changed", await PackVerifier.ReferenceProblemAsync(resource, Roots, Cancel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reference_RootNotConfiguredOrFileGone_IsUnavailable()
    {
        _folder.Write("share/macros/Motor.ema", "<EplanPxfRoot />");
        PackResource resource = await ReferencedAsync("macros/Motor.ema");

        Assert.Contains("does not list", await PackVerifier.ReferenceProblemAsync(resource, new Dictionary<string, string>(), Cancel), StringComparison.Ordinal);
        File.Delete(_folder.PathOf("share/macros/Motor.ema"));
        Assert.Contains("is missing", await PackVerifier.ReferenceProblemAsync(resource, Roots, Cancel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reference_ThroughAJunction_IsUnavailable()
    {
        _folder.Write("elsewhere/Motor.ema", "<EplanPxfRoot />");
        _folder.Junction("share/macros", _folder.PathOf("elsewhere"));
        PackResource resource = Manifests.File("resources/macros/Motor.ema", ResourceKind.Macro) with
        {
            Storage = ResourceStorage.Referenced,
            Reference = new ResourceReference { Root = "company-refs", Path = "macros/Motor.ema" },
        };

        Assert.Contains("junction", await PackVerifier.ReferenceProblemAsync(resource, Roots, Cancel), StringComparison.Ordinal);
    }

    public void Dispose() => _folder.Dispose();

    private async Task<PackResource> ReferencedAsync(string path)
    {
        (string sha256, long bytes) = await PackHasher.HashFileAsync(_folder.PathOf("share/" + path), Cancel);
        return Manifests.File("resources/macros/Motor.ema", ResourceKind.Macro) with
        {
            Sha256 = sha256,
            Bytes = bytes,
            Storage = ResourceStorage.Referenced,
            Reference = new ResourceReference { Root = "company-refs", Path = path },
        };
    }
}
