using System.Text.Json;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// The export tools as serve registers them follow ADR-0012: Read tier, yet they write files, so readOnlyHint,
/// destructiveHint and idempotentHint are all false; and no argument names a folder (SEC-20).
/// </summary>
[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class ExportToolContractTests
{
    [Fact]
    public void ExportTools_AreAnnotatedAsAdr0012Says_AndTakeNoFolder()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        Tool[] exports = [.. provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Select(t => t.ProtocolTool).Where(t => t.Name.StartsWith("eplan_export_", StringComparison.Ordinal))];

        Assert.Equal([ExportTools.DxfTool, ImageExportTools.ImageTool, ProjectExportTools.LabelsTool, ProjectExportTools.PartsTool, ExportTools.PdfTool, ProjectExportTools.PxfTool], exports.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(exports, t =>
        {
            Assert.Equal((false, false, false, false), (t.Annotations!.ReadOnlyHint, t.Annotations.DestructiveHint, t.Annotations.IdempotentHint, t.Annotations.OpenWorldHint));
            string[] arguments = [.. t.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name)];
            Assert.DoesNotContain(arguments, a => a.Contains("dir", StringComparison.Ordinal) || a.Contains("path", StringComparison.Ordinal));
            Assert.Equal(t.Name == ImageExportTools.ImageTool ? ["pages"] : t.Name == ProjectExportTools.LabelsTool ? ["scheme"] : [], t.InputSchema.TryGetProperty("required", out JsonElement required) ? required.EnumerateArray().Select(r => r.GetString()) : []);
        });
    }
}
