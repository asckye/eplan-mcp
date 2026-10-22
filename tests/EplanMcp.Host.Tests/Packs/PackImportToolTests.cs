using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>
/// eplan_pack_import on synthetic inputs in a temporary data folder (docs/tools/eplan_pack_import.md, sections 2 to 7);
/// no EPLAN file is committed (CLAUDE.md rule 4).
/// </summary>
[Trait("Size", "Medium")]
public sealed class PackImportToolTests : IDisposable
{
    private const string Macro = "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-pack-import-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly HostConfig _config;

    public PackImportToolTests()
    {
        _config = HostConfig.Defaults(_locations) with
        {
            PackSources = new Dictionary<string, string> { ["company-refs"] = Path.Combine(_locations.Root, "share") },
        };
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_NewPack_AddsResourcesAndAudits()
    {
        Write("imports/macros/Motor.ema", Macro);
        Write("imports/company/rules.md", "# Rules");

        CallToolResult result = await ImportAsync(["macros", "rule=company/rules.md"], name: "ACME panels");

        ToolResultAssert.Succeeded(result);
        JsonElement data = result.StructuredContent!.Value;
        Assert.True(data.GetProperty("created").GetBoolean());
        Assert.Equal(["acme.panels@dev:macro/macros/Motor.ema", "acme.panels@dev:rule/rules.md"], data.GetProperty("added").EnumerateArray().Select(a => a.GetProperty("asset").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal("Imported 2 resources into development pack 'acme.panels'.", ((TextContentBlock)result.Content[0]).Text);
        PackCheckResult check = await PackVerifier.CheckDirectoryAsync(Path.Combine(_locations.PacksDev, "acme.panels"), Cancel);
        Assert.Empty(check.Problems);
        Assert.Equal(KnowledgeStanding.Candidate, Assert.Single(check.Manifest!.Rules).Standing);
        Assert.Equal(["started", "committed"], AuditPhases());
        Assert.Equal(data.GetProperty("digest").GetString(), AuditRecords().Last()["digest"]!.GetValue<string>());
    }

    [Fact]
    public async Task Import_ProjectInImportsWithoutBundling_IsRefusedAndWritesNothing()
    {
        WriteProject("imports/refs/Plant");

        CallToolResult result = await ImportAsync(["refs"]);

        Error(result, ErrorReason.InvalidArgument, "bundle_projects");
        Assert.False(Directory.Exists(_locations.PacksDev));
        Assert.Empty(AuditPhases());
    }

    [Fact]
    public async Task Import_ProjectBundledWithConsent_KeepsSessionFiles()
    {
        WriteProject("imports/refs/Plant");
        Write("imports/refs/Plant/Plant.edb/User.eod", "session");

        CallToolResult result = await ImportAsync(["refs"], bundleProjects: true);

        ToolResultAssert.Succeeded(result);
        Assert.True(File.Exists(Path.Combine(_locations.PacksDev, "acme.panels", "resources", "projects", "refs", "Plant", "Plant.edb", "User.eod")));
        Assert.StartsWith("acme.panels@dev:gap/eplan-compatibility-unchecked-", result.StructuredContent!.Value.GetProperty("gaps")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_ProjectBelowAPackSource_IsReferenced()
    {
        WriteProject("share/Projects/Plant");

        CallToolResult result = await ImportAsync(["company-refs:Projects/Plant"]);

        ToolResultAssert.Succeeded(result);
        JsonElement added = Assert.Single(result.StructuredContent!.Value.GetProperty("added").EnumerateArray());
        Assert.Equal(("referenced", "acme.panels@dev:project/Plant/Plant.elk"), (added.GetProperty("storage").GetString(), added.GetProperty("asset").GetString()));
    }

    [Fact]
    public async Task Import_ChangedFile_IsAlreadyExistsUntilReplaced()
    {
        Write("imports/macros/Motor.ema", Macro);
        await ImportAsync(["macros"]);
        Write("imports/macros/Motor.ema", Macro + " ");

        Error(await ImportAsync(["macros"]), ErrorReason.AlreadyExists, "replace");
        CallToolResult replaced = await ImportAsync(["macros"], replace: true);

        Assert.Single(replaced.StructuredContent!.Value.GetProperty("replaced").EnumerateArray());
        Assert.Equal(["started", "committed", "started", "failed", "started", "committed"], AuditPhases());
    }

    [Theory]
    [InlineData("C:\\imports\\x", "no drive")]
    [InlineData("other:x", "does not list")]
    [InlineData("tools/run.ps1", "executable or script")]
    public async Task Import_BadInputs_AreInvalidArguments(string input, string reason)
    {
        Write("imports/tools/run.ps1", "Remove-Item");

        Error(await ImportAsync([input]), ErrorReason.InvalidArgument, reason);
    }

    [Fact]
    public async Task Import_MissingInput_IsFileNotFoundWithoutExecution()
    {
        CallToolResult result = await ImportAsync(["nothing-here"]);

        JsonElement error = Error(result, ErrorReason.FileNotFound, "nothing-here");
        Assert.Equal(JsonValueKind.Null, error.GetProperty("execution").ValueKind);
    }

    [Fact]
    public async Task Import_AuditUnavailable_ChangesNothing()
    {
        Write("imports/macros/Motor.ema", Macro);
        File.WriteAllText(_locations.Audit, "a file where the audit folder belongs");

        Error(await ImportAsync(["macros"]), ErrorReason.AuditUnavailable, "nothing was changed");

        Assert.False(Directory.Exists(_locations.PacksDev));
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    private Task<CallToolResult> ImportAsync(string[] inputs, string? name = null, bool bundleProjects = false, bool replace = false)
    {
        var packs = new PackLibrary(_locations, _config, new AuditLog(_locations.Audit, _time), _time);
        var installer = new PackInstaller(packs, new ApprovalService([], _config, _time, NullLogger<ApprovalService>.Instance), new FakeBridgeClient());
        return PackTools.PackImportAsync(packs, new GapOnlyReferenceProjectSteps(), installer, "acme.panels", inputs, name, bundleProjects, replace, null, Cancel);
    }

    private static JsonElement Error(CallToolResult result, string reason, string text)
    {
        Assert.True(result.IsError);
        JsonElement error = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
        Assert.Equal(reason, error.GetProperty("reason").GetString());
        Assert.Contains(text, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        return error;
    }

    private List<JsonObject> AuditRecords() => !Directory.Exists(_locations.Audit) ? [] :
        [.. Directory.EnumerateFiles(_locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadLines).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject())];

    private List<string> AuditPhases() => [.. AuditRecords().Where(r => r["kind"]!.GetValue<string>() == PackLibrary.AuditKind).Select(r => r["phase"]!.GetValue<string>())];

    private void Write(string relative, string content)
    {
        string path = Path.Combine(_locations.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteProject(string folder)
    {
        string name = Path.GetFileName(folder);
        Write($"{folder}/{name}.elk", $"{name}.edb\n18.0\n");
        Write($"{folder}/{name}.edb/ProjectInfo.xml", "<Properties />");
        Write($"{folder}/{name}.edb/Page.eod", "pages");
    }
}
