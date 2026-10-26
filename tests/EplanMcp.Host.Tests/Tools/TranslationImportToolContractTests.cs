using EplanMcp.Contracts;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class TranslationImportToolContractTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MasterDataApproval_IsNeverAutoAllowed(bool destructive, bool inScratch)
    {
        var config = new HostConfig([@"C:\Scratch"], OutsideScratchWrites.Approve);
        var target = new MasterDataTarget { Kind = MasterDataKinds.Translations, Database = @"C:\Scratch\Translate.db" };

        Assert.Equal(PolicyVerdict.NeedsApproval, PolicyEngine.Decide(destructive ? ToolTier.Destructive : ToolTier.Write, inScratch, config, target).Verdict);
    }

    [Fact]
    public void Contract_ExposesDestructiveAnnotationsAndTheReferenceLanguageDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Single(t => t.ProtocolTool.Name == TranslationImportTools.Tool).ProtocolTool;

        Assert.Equal((false, true, false, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint, tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
        Assert.Equal("en_US", tool.InputSchema.GetProperty("properties").GetProperty("reference_language").GetProperty("default").GetString());
        Assert.Equal(["file", "reference_language", "dry_run"], tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Contains("engineer's approval", tool.Description, StringComparison.Ordinal);
        Assert.Contains("never rewritten", tool.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../glossary.txt")]
    [InlineData("C:/glossary.txt")]
    [InlineData("glossary.csv")]
    [InlineData("")]
    public void ImportPath_UsesTheSharedImportRules(string file) => Assert.NotNull(ImportFiles.Problem("file", file, [".txt"]));
}
