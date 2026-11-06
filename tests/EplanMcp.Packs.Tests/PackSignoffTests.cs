namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PackSignoffTests
{
    private static readonly string[] _signingFields = ["signedBy", "signedAt"];

    [Fact]
    public void Signoff_IsOptionalAndRoundTripsWithoutChangingTheRecord()
    {
        PackManifest unsigned = PackJson.ReadManifest(PackJson.Serialize(Manifests.Minimal()));
        Assert.Null(unsigned.Signoff);
        Assert.Empty(PackManifestValidator.Validate(unsigned));
        var signoff = new PackSignoff { Status = "signed", SignedBy = "Engineer", SignedAt = "2026-11-06T12:00:00Z" };
        PackManifest signed = PackJson.ReadManifest(PackJson.Serialize(unsigned with { Signoff = signoff }));
        Assert.Equal(signoff, signed.Signoff);
        Assert.Empty(PackManifestValidator.Validate(signed));
    }

    [Fact]
    public void Draft_DoesNotRequireSigningDetails() =>
        Assert.Empty(PackManifestValidator.Validate(Manifests.Minimal() with { Signoff = new() { Status = "draft" } }));

    [Fact]
    public void Schema_RequiresSigningDetailsForSignedPacks()
    {
        var signoff = PackSchema.Manifest()["properties"]!["signoff"]!;
        Assert.Equal("^(draft|signed)$", signoff["properties"]!["status"]!["pattern"]!.GetValue<string>());
        Assert.Equal(_signingFields, signoff["allOf"]![0]!["then"]!["required"]!.AsArray().Select(item => item!.GetValue<string>()));
    }

    [Theory]
    [InlineData("unknown", "Engineer", "2026-11-06", "signoff.status")]
    [InlineData("signed", null, "2026-11-06", "signoff.signedBy")]
    [InlineData("signed", "", "2026-11-06", "signoff.signedBy")]
    [InlineData("signed", "Engineer", null, "signoff.signedAt")]
    [InlineData("signed", "Engineer", "", "signoff.signedAt")]
    public void InvalidSignoff_NamesTheField(string status, string? by, string? at, string location)
    {
        var manifest = Manifests.Minimal() with { Signoff = new() { Status = status, SignedBy = by, SignedAt = at } };
        Assert.Contains(PackManifestValidator.Validate(manifest), problem => problem.Location == location);
    }
}
