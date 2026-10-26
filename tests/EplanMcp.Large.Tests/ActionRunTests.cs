using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>S26's exact parameter combinations through the product pipelines; never run without scratch fixtures.</summary>
[Trait("Size", "Large")]
public sealed class ActionRunTests(LiveEplan eplan) : IAsyncLifetime
{
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-large-actions-").FullName;
    private ServiceProvider? _services;
    private ActionRunTools? _tool;
    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.ClearProviders());
        services.AddSingleton(new HostDataLocations(_home));
        services.AddSingleton(new HostConfig([LiveEplan.ScratchRoot()], OutsideScratchWrites.Deny) { ActionRun = ActionRunSwitch.On });
        services.AddSingleton(new ActionRunCatalog());
        services.AddEplanMcp();
        _services = services.BuildServiceProvider();
        await _services.GetRequiredService<IBridgeClient>().SelectAsync(eplan.ProcessId, TestContext.Current.CancellationToken);
        _tool = ActivatorUtilities.CreateInstance<ActionRunTools>(_services);
    }
    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        { await _services.DisposeAsync(); }
        Directory.Delete(_home, recursive: true);
    }

    [Theory]
    [InlineData("graphicallayertable/EXPORT", "{\"EXPORTFILE\":\"layers.elc\"}", 133128)]
    [InlineData("projectmanagement/EXPORTPROPERTYPLACEMENTSSCHEMAS", "{\"FILENAME\":\"placements.xml\"}", 1598040)]
    [InlineData("XEsUserPropertiesExportAction", "{\"XMLFile\":\"properties.xml\"}", 457275)]
    [InlineData("plcservice/BUSDATAEXPORT", "{\"CONFIGURATIONPROJECT\":\"Phoenix1\",\"CONVERTERID\":\"PlcDcXMLExchangerUniversal\",\"DESTINATIONFILE\":\"bus.pbf\"}", 431387)]
    [InlineData("plcservice/BUSDATAEXPORT", "{\"CONFIGURATIONPROJECT\":\"Phoenix1\",\"CONVERTERID\":\"PlcDcAMLExchangerGeneral\",\"DESTINATIONFILE\":\"bus.aml\"}", 95787)]
    [InlineData("plcservice/BUSDATAEXPORT", "{\"CONFIGURATIONPROJECT\":\"Phoenix1\",\"CONVERTERID\":\"PlcDcAMLExchangerGeneral\",\"DESTINATIONFILE\":\"bus.aml\",\"FORMAT\":\"AutomationML AR APC V1.4.0\"}", 104341)]
    public async Task ProjectExport_LeavesOneFileAndUnchangedCounts(string entry, string arguments, int s26Bytes)
    {
        string project = await eplan.WriteProjectAsync();
        JsonElement before = await Counts(project);
        JsonElement result = Success(await Call(entry, arguments, project));
        CheckFile(result, s26Bytes);
        Assert.Equal(before.GetRawText(), (await Counts(project)).GetRawText());
        Assert.Equal(result.GetProperty("before").GetProperty("counts").GetRawText(), result.GetProperty("after").GetProperty("counts").GetRawText());
        // AML's file changes are unresolved, so report/disable them, never whitelist them as harmless.
        if (entry == "plcservice/BUSDATAEXPORT" && arguments.Contains("PlcDcAMLExchangerGeneral", StringComparison.Ordinal))
        { TestContext.Current.TestOutputHelper?.WriteLine("AML entryDisabled=" + result.GetProperty("entryDisabled") + "; " + result.GetProperty("hint")); }
        Assert.True((await eplan.CallAsync("eplan_session_status")).GetProperty("connected").GetBoolean());
    }

    [Theory]
    [InlineData("EXPORTPARTS", 22001, "10 20 206", 6834)]
    [InlineData("EXPORTMANUFACTURERS", 22900, "ABB", 967)]
    [InlineData("EXPORTCONSTRUCTIONS", 22931, "A-B.100-C_FS0_I_CH_DP", 1019)]
    [InlineData("EXPORTCONNECTIONPATTERNS", 22945, "3RH2911-1FA22-0MA0", 9480)]
    [InlineData("EXPORTACCESSORYLISTS", 22960, "A-B.100-C_FS0_ACC", 598)]
    [InlineData("EXPORTACCESSORYPLACEMENTS", 22970, "RIT.AX_Handle", 940)]
    public async Task PartsExport_RequiresTheVerifiedFilterAndDoesNotChangeProjectCounts(string type, int id, string value, int s26Bytes)
    {
        string project = await eplan.WriteProjectAsync();
        JsonElement before = await Counts(project);
        string arguments = JsonSerializer.Serialize(new { EXPORTFILE = "parts.xml", PROPERTYID = id.ToString(System.Globalization.CultureInfo.InvariantCulture), PROPERTYVALUE = value });
        JsonElement result = Success(await Call("partsmanagementapi/" + type, arguments, null));
        CheckFile(result, s26Bytes);
        Assert.Equal(before.GetRawText(), (await Counts(project)).GetRawText());
        Assert.False(result.GetProperty("entryDisabled").GetBoolean());
    }

    [Theory]
    [InlineData("ARTICLES", ".zw6")]
    [InlineData("LANGUAGES", ".zw7")]
    public async Task MasterDataBackup_IsOneNonEmptySevenZipArchive(string type, string extension)
    {
        string project = await eplan.WriteProjectAsync();
        JsonElement before = await Counts(project);
        JsonElement result = Success(await Call("backup/MASTERDATA", JsonSerializer.Serialize(new { MDTYPE = type, ARCHIVENAME = "LibraryBackup" }), null));
        CheckFile(result, 0);
        Assert.EndsWith(extension, result.GetProperty("files")[0].GetProperty("path").GetString(), StringComparison.Ordinal);
        Assert.Equal(before.GetRawText(), (await Counts(project)).GetRawText());
    }

    [Fact]
    public async Task MissingPartsFilter_IsRefusedByHostWithoutCreatingAnExportFolder()
    {
        JsonElement error = Error(await Call("partsmanagementapi/EXPORTPARTS", "{\"EXPORTFILE\":\"parts.xml\"}", null));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("PROPERTYID", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Contains("PROPERTYVALUE", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_home, "exports")));
    }

    [Fact]
    public async Task ChangeLayer_LargeWriteChangesAndTheResultReportsTheObservedVisibility()
    {
        string project = await eplan.WriteProjectAsync();
        Assert.Equal("LargeWrite", Path.GetFileName(project));
        var bridge = _services!.GetRequiredService<IBridgeClient>();
        var args = new ActionRunArgs { Entry = "changelayer", Project = project, Parameters = [new() { Name = "LAYER1", Value = "EPLAN100" }, new() { Name = "VISIBLE1", Value = "0" }] };
        WritePlan plan = await bridge.InvokeAsync<ActionRunArgs, WritePlan>(OperationNames.ActionRunPlan, args, TestContext.Current.CancellationToken);
        ActionRunData before = JsonSerializer.Deserialize<ActionRunData>(plan.Details!["actionRun"], HostJson.Options)!;
        Assert.True(before.Before["layer:visible"] == "1", "Prepare a fresh LargeWrite copy with EPLAN100 visible, as S26 did.");
        try
        {
            JsonElement result = Success(await Call("changelayer", "{\"LAYER\":\"EPLAN100\",\"VISIBLE\":\"0\"}", project));
            Assert.Equal("committed", result.GetProperty("execution").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("undo").ValueKind);
            Assert.Contains("Layer visibility: 1 -> 0", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
            WritePlan after = await bridge.InvokeAsync<ActionRunArgs, WritePlan>(OperationNames.ActionRunPlan, args, TestContext.Current.CancellationToken);
            Assert.Equal("0", JsonSerializer.Deserialize<ActionRunData>(after.Details!["actionRun"], HostJson.Options)!.Before["layer:visible"]);
        }
        finally { Success(await Call("changelayer", "{\"LAYER\":\"EPLAN100\",\"VISIBLE\":\"1\"}", project)); }
        TestContext.Current.TestOutputHelper?.WriteLine("Commit followed by single Ctrl+Z and prior undo history still require engineer observation; restoration here is another tool call.");
    }
    [Theory]
    [InlineData("synchronize/MULTILINE")]
    [InlineData("synchronize/OVERVIEW")]
    public Task Synchronize_CommittedThenOneUndoRestoresTextsAndEarlierHistory(string entry) => VerifyPromotionAsync(entry);

    [Fact]
    public async Task SwitchProjectType_CommittedThenOneUndoRestoresTypeAndEarlierHistoryWithoutChangingOtherProjects()
    {
        string project = await eplan.WriteProjectAsync();
        Dictionary<string, string> others = await OtherProjectTypesAsync(project);
        try
        {
            await VerifyPromotionAsync("SwitchProjectType");
        }
        finally
        {
            // The selection helper has reopened only the fixture projects it closed (A41/A46).
            Assert.Equal(others.OrderBy(p => p.Key, StringComparer.Ordinal), (await OtherProjectTypesAsync(project)).OrderBy(p => p.Key, StringComparer.Ordinal));
        }
    }

    private async Task VerifyPromotionAsync(string entry)
    {
        // A41/A46: select LargeWrite through the existing scratch-only helper; every undo checks current.
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string project = selection.Project;
        Assert.Equal("LargeWrite", Path.GetFileName(project));
        Assert.StartsWith(Path.Combine(LiveEplan.ScratchRoot(), "writes") + Path.DirectorySeparatorChar, project, StringComparison.OrdinalIgnoreCase);
        bool switchType = entry == "SwitchProjectType";
        string inspection = switchType ? "graphicallayertable/EXPORT" : entry;
        if (switchType)
        {
            Assert.True((await OtherProjectTypesAsync(project)).Keys.Any(
                p => p.StartsWith(Path.Combine(LiveEplan.ScratchRoot(), "writes") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)),
                "Open another scratch writes copy sorting after LargeWrite, such as S26Type, to verify isolation while switching type.");
        }
        string[] sentinel = await selection.CreatePagesAsync(1);
        ActionRunData before = await InspectAsync(inspection, project);
        string state = await selection.StateAsync();
        Dictionary<string, string> others = await OtherProjectTypesAsync(project);
        if (switchType)
        {
            Assert.Equal("Schematic", before.Before["project:type"]);
        }
        try
        {
            JsonElement result = Success(await Call(entry, switchType ? "{\"PROJECTTYPE\":\"2\"}" : "{}", project));
            Assert.Equal("committed", result.GetProperty("execution").GetString());
            Assert.False(result.GetProperty("recovered").GetBoolean());
            Assert.Equal("write", result.GetProperty("tier").GetString());
            ActionRunData after = await InspectAsync(inspection, project);
            if (switchType)
            {
                Assert.Equal("Macro", after.Before["project:type"]);
                Assert.Equal("Schematic", result.GetProperty("before").GetProperty("projectType").GetString());
                Assert.Equal("Macro", result.GetProperty("after").GetProperty("projectType").GetString());
                Assert.Contains("Project type: Schematic -> Macro", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
                Assert.Equal(others.OrderBy(p => p.Key, StringComparer.Ordinal), (await OtherProjectTypesAsync(project)).OrderBy(p => p.Key, StringComparer.Ordinal));
            }
            else
            {
                string hash = before.Before["synchronize:functionTextHash"];
                Assert.NotEqual(hash, after.Before["synchronize:functionTextHash"]);
                Assert.Equal(hash, result.GetProperty("before").GetProperty("functionTextHash").GetString());
                Assert.Equal(after.Before["synchronize:functionTextHash"], result.GetProperty("after").GetProperty("functionTextHash").GetString());
                Assert.NotEqual(Texts(before), Texts(after));
                foreach (string kind in new[] { "function_texts", "project_parts", "part_references" })
                {
                    JsonElement observed = Assert.Single(result.GetProperty("after").GetProperty("counts").EnumerateArray(), c => c.GetProperty("kind").GetString() == kind);
                    Assert.Equal(after.Before["count:" + kind], observed.GetProperty("count").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            await selection.UndoAsync();
            ActionRunData restored = await InspectAsync(inspection, project);
            Assert.Equal(Business(before), Business(restored));
            Assert.Equal(state, await selection.StateAsync());
            Assert.Contains((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
            if (switchType)
            {
                Assert.Equal(others.OrderBy(p => p.Key, StringComparer.Ordinal), (await OtherProjectTypesAsync(project)).OrderBy(p => p.Key, StringComparer.Ordinal));
            }

            // Exactly one more undo must remove the earlier sentinel, proving no extra action undo steps.
            await selection.UndoAsync();
            Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
        }
        finally
        {
            if (switchType)
            {
                ActionRunData current = await InspectAsync(inspection, project);
                if (current.Before["project:type"] != "Schematic")
                {
                    JsonElement restored = Success(await Call(entry, "{\"PROJECTTYPE\":\"1\"}", project));
                    Assert.Equal("committed", restored.GetProperty("execution").GetString());
                }
                Assert.Equal("Schematic", (await InspectAsync(inspection, project)).Before["project:type"]);
            }
        }
    }

    private async Task<ActionRunData> InspectAsync(string entry, string project)
    {
        var args = new ActionRunArgs { Entry = entry, Project = project };
        if (entry == "graphicallayertable/EXPORT")
        {
            args.Parameters.Add(new() { Name = "EXPORTFILE", Value = "inspect.elc" });
        }
        WritePlan plan = await _services!.GetRequiredService<IBridgeClient>().InvokeAsync<ActionRunArgs, WritePlan>(OperationNames.ActionRunPlan, args, TestContext.Current.CancellationToken);
        return JsonSerializer.Deserialize<ActionRunData>(plan.Details!["actionRun"], HostJson.Options)!;
    }

    private async Task<Dictionary<string, string>> OtherProjectTypesAsync(string target)
    {
        ProjectListData open = await _services!.GetRequiredService<IBridgeClient>().InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), TestContext.Current.CancellationToken);
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (OpenProject project in open.Projects.Where(p => !string.Equals(p.Path, target, StringComparison.OrdinalIgnoreCase)))
        {
            types.Add(project.Path, (await InspectAsync("graphicallayertable/EXPORT", project.Path)).Before["project:type"]);
        }
        return types;
    }

    private static string Texts(ActionRunData data) => JsonSerializer.Serialize(data.Before.Where(p => p.Key.StartsWith("function:text:", StringComparison.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal));
    private static string Business(ActionRunData data) => JsonSerializer.Serialize(data.Before.Where(p => !p.Key.StartsWith("file:", StringComparison.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal));

    private Task<CallToolResult> Call(string entry, string arguments, string? project, bool dry = false) => _tool!.RunAsync(new Progress<ProgressNotificationValue>(), entry, JsonDocument.Parse(arguments).RootElement, project, dry, TestContext.Current.CancellationToken);
    private async Task<JsonElement> Counts(string project) => Success(await Call("graphicallayertable/EXPORT", "{\"EXPORTFILE\":\"count-probe.elc\"}", project, dry: true)).GetProperty("before").GetProperty("counts");
    private static JsonElement Success(CallToolResult result)
    {
        Assert.False(result.IsError == true, string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text)));
        return result.StructuredContent!.Value;
    }
    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
    }
    private static void CheckFile(JsonElement result, int s26Bytes)
    {
        Assert.Equal(1, result.GetProperty("fileCount").GetInt32());
        JsonElement file = Assert.Single(result.GetProperty("files").EnumerateArray());
        string path = Path.Combine(result.GetProperty("directory").GetString()!, file.GetProperty("path").GetString()!);
        Assert.True(file.GetProperty("bytes").GetInt64() > 0);
        Assert.Equal(new FileInfo(path).Length, file.GetProperty("bytes").GetInt64());
        using FileStream stream = File.OpenRead(path);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(stream)), file.GetProperty("sha256").GetString());
        TestContext.Current.TestOutputHelper?.WriteLine($"S26 original fixture baseline {s26Bytes} bytes; this configured fixture {file.GetProperty("bytes")} bytes. Product wrapping/dialog observation remains a release check.");
    }
}
