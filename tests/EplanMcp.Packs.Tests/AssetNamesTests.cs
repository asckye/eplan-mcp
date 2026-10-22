using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class AssetNamesTests
{
    [Theory]
    [InlineData("resources/macros/In/Motor.ema", ResourceKind.Macro, "macro/In/Motor.ema")]
    [InlineData("resources/projects/Refs/Plant/Plant.elk", ResourceKind.Project, "project/Refs/Plant/Plant.elk")]
    [InlineData("resources/backups/Plant.zw1", ResourceKind.ProjectBackup, "project_backup/Plant.zw1")]
    [InlineData("elsewhere/notes.md", ResourceKind.Document, "document/elsewhere/notes.md")]
    [InlineData("knowledge/candidate/motor-overload.json", ResourceKind.Rule, "candidate/motor-overload")]
    [InlineData("knowledge/material_mapping/motors.json", ResourceKind.MaterialMapping, "material_mapping/motors")]
    public void Local_NamesTheResourceByKindAndPath(string path, ResourceKind kind, string local)
    {
        Assert.Equal(local, AssetNames.Local(Manifests.File(path, kind)));
    }

    [Theory]
    [InlineData("acme.panels@dev:macro/In/Motor.ema", "acme.panels", "dev", "macro/In/Motor.ema")]
    [InlineData("acme.panels@1.2.0:candidate/motor-overload", "acme.panels", "1.2.0", "candidate/motor-overload")]
    public void TryParse_SplitsAnAssetId(string id, string pack, string version, string local)
    {
        Assert.True(AssetNames.TryParse(id, out string? p, out string? v, out string? l));
        Assert.Equal((pack, version, local), (p, v, l));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("acme.panels:macro/x.ema")]
    [InlineData("Acme@dev:macro/x.ema")]
    [InlineData("acme@dev:macro")]
    [InlineData("acme@dev:Macro/x.ema")]
    public void TryParse_OtherText_IsNoAssetId(string? id)
    {
        Assert.False(AssetNames.TryParse(id, out _, out _, out _));
    }

    [Fact]
    public void KnowledgeDocument_PathAndManifestKind_FollowItsKind()
    {
        Assert.Equal("knowledge/material_mapping/motors.json", KnowledgeDocument.PathOf(KnowledgeKind.MaterialMapping, "motors"));
        Assert.Equal(ResourceKind.Rule, KnowledgeDocument.ResourceKindOf(KnowledgeKind.Candidate));
    }
}
