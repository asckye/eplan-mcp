using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
[Trait("Spike", "C7")]
public sealed class CablePartFunctionsSpikeTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    private const string Model = "O:Eplan.EplApi.DataModel.";
    private const string Master = "O:Eplan.EplApi.MasterData.";
    private ApiTryTools _api = null!;

    [Fact]
    public async Task ToolCable_WithPart_FunctionsAndCheckMessages()
    {
        string folder = Directory.CreateTempSubdirectory("eplanmcp-c7-").FullName;
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new HostDataLocations(folder));
            services.AddSingleton(new HostConfig([LiveEplan.ScratchRoot()], OutsideScratchWrites.Deny) { ApiTry = ActionRunSwitch.On });
            services.AddEplanMcp();
            services.RemoveAll<IApprovalChannel>();
            services.AddSingleton<IApprovalChannel>(new TrialApprovals(ApiTryTools.Tool));
            await using ServiceProvider provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IBridgeClient>().SelectAsync(eplan.ProcessId, TestContext.Current.CancellationToken);
            _api = ActivatorUtilities.CreateInstance<ApiTryTools>(provider);
            await using (PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan))
            {
                string project = session.Project;
                string page = (await session.CreatePagesAsync(1)).Single();
                // Live 2026-11-19: a third client beside the host's and the provider's found no EPLAN; reuse the provider's.
                var bridge = (BridgeClient)provider.GetRequiredService<IBridgeClient>();
                var (created, creation) = await CablePage.CreateAsync(host, bridge, project, page);
                Log("S0 creation", creation);
                if (!created)
                {
                    return;
                }
                PageDrawingGeometry setup = await CablePage.ReadAsync(bridge, project, page);
                DrawingPlacement? cable = setup.Placements.FirstOrDefault(row => row.Kind == "Cable");
                if (cable?.Properties.GetValueOrDefault("name") is not { Length: > 0 } tag)
                {
                    Log("S0", "No named cable definition line on the scratch page.");
                    return;
                }
                await ObserveAsync("S0", project, page, tag, runCheck: true);

                var part = await ChoosePartAsync(project);
                if (part is null)
                {
                    Log("S1", "No cable part with at least four cores was found.");
                    return;
                }
                var update = new Dictionary<string, object?> { ["project"] = project, ["tag"] = tag, ["dry_run"] = false };
                await WriteAsync("S1", "eplan_device_update", new(update) { ["part"] = part.Value.Number, ["variant"] = part.Value.Variant });
                await ObserveAsync("S1", project, page, tag, runCheck: true);
                // Live 2026-11-19, 3G part on three wires without PE potential: two cores were taken after
                // update_from_parts, the third stayed free and the check kept its message. This run uses a part
                // with a spare core and asks whether reassigning alone assigns the cores.
                var reassign = new Dictionary<string, object?> { ["project"] = project, ["cables"] = new[] { tag } };
                var (planned, preview) = await host.CallOrErrorAsync("eplan_cable_reassign", reassign);
                Log("S2 preview", preview);
                if (planned)
                {
                    host.Approvals.Approve("eplan_cable_reassign", project,
                        [$"update cables {WriteHost.WritableName}: cores '{preview.GetProperty("scope").GetString()}' -> 'assigned by EPLAN'"]);
                    await WriteAsync("S2", "eplan_cable_reassign", new(reassign) { ["dry_run"] = false });
                }
                await ObserveAsync("S2", project, page, tag, runCheck: true);
                await WriteAsync("S3", "eplan_device_update", new(update) { ["update_from_parts"] = true });
                await ObserveAsync("S3", project, page, tag, runCheck: true);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private async Task<(string Number, string Variant)?> ChoosePartAsync(string project)
    {
        var projects = await FindAsync(project, "Project");
        if (projects.Count == 0)
        {
            return null;
        }
        JsonElement anchor = projects[0].GetProperty("locator");
        JsonElement? database = await ApiAsync(project, Master + "MDPartsManagement.OpenDatabase", new { target = anchor });
        var seen = new HashSet<(string, string)>();
        foreach (string query in new[] { "ÖLFLEX", "cable", "Kabel", "LAPP" })
        {
            string? cursor = null;
            do
            {
                var (found, search) = await host.CallOrErrorAsync("eplan_part_search", new() { ["query"] = query, ["limit"] = 100, ["cursor"] = cursor });
                Log("S1 part search " + query, search);
                if (!found)
                {
                    break;
                }
                foreach (JsonElement part in search.GetProperty("parts").EnumerateArray())
                {
                    string number = part.GetProperty("partNumber").GetString()!;
                    string variant = part.GetProperty("variant").GetString() ?? "1";
                    string group = part.GetProperty("productGroup").GetString() ?? string.Empty;
                    if (group != "ElectricalCableConnection" || !seen.Add((number, variant)) || database is null)
                    {
                        continue;
                    }
                    JsonElement? handle = await ApiAsync(project, Master + "MDPartsDatabase.GetPart", new { target = database, partNr = number, variant });
                    if (handle is null)
                    {
                        continue;
                    }
                    var templates = await CollectionAsync(project, Master + "MDPart.FunctionTemplatePositions", handle.Value);
                    Log("S1 candidate", new { number, variant, group, templateCount = templates?.Count });
                    if (templates?.Count >= 5)
                    {
                        Log("S1 selected", part);
                        return (number, variant);
                    }
                }
                cursor = search.GetProperty("nextCursor").GetString();
            } while (cursor is not null);
        }
        return null;
    }

    private async Task ObserveAsync(string stage, string project, string page, string tag, bool runCheck)
    {
        Log(stage, new { project, page, tag });
        var kinds = ApiTryCatalog.Kinds("Eplan.EplApi.DataModel.Function").ToHashSet(StringComparer.OrdinalIgnoreCase);
        int functionCount = 0;
        foreach (string kind in ObjectReadTypes.Types.Where(k => kinds.Contains(k) && k is not ("PLCTerminal" or "BlackBox")))
        {
            foreach (JsonElement item in await FindAsync(project, kind, name: tag))
            {
                string? name = item.GetProperty("name").GetString();
                if (name == tag || name?.StartsWith(tag + ":", StringComparison.Ordinal) == true)
                {
                    functionCount++;
                    JsonElement target = item.GetProperty("locator");
                    await FunctionAsync(stage + " project function", project, kind, target);
                    if (kind == "Cable" && name == tag && target.GetProperty("page").GetString() == page)
                    {
                        await TemplatesAsync(stage, project, target);
                    }
                }
            }
        }
        var points = await FindAsync(project, "ConnectionDefinitionPoint", page);
        Log(stage + " observed counts", new { projectFunctions = functionCount, pageDefinitionPoints = points.Count });
        foreach (JsonElement item in points)
        {
            await FunctionAsync(stage + " page definition point", project, "ConnectionDefinitionPoint", item.GetProperty("locator"));
        }
        if (runCheck)
        {
            await WriteAsync(stage, "eplan_check_run", new() { ["project"] = project, ["pages"] = new[] { page }, ["dry_run"] = false });
        }
        string? cursor = null;
        int index = tag.LastIndexOf('-');
        string visible = index >= 0 ? tag[index..] : tag;
        int count = 0;
        do
        {
            var (listed, list) = await host.CallOrErrorAsync("eplan_check_list", new() { ["project"] = project, ["limit"] = 200, ["include_done"] = true, ["cursor"] = cursor });
            if (!listed)
            {
                Log(stage + " check list unavailable", list);
                break;
            }
            foreach (JsonElement message in list.GetProperty("messages").EnumerateArray())
            {
                string text = message.GetProperty("text").ToString();
                string subject = message.GetProperty("object").ToString();
                if (text.Contains(visible, StringComparison.OrdinalIgnoreCase) || subject.Contains(visible, StringComparison.OrdinalIgnoreCase))
                {
                    Log(stage + " check message", message);
                    count++;
                }
            }
            cursor = list.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Log(stage + " matching messages", count);
    }

    private async Task FunctionAsync(string label, string project, string kind, JsonElement target)
    {
        var receiver = new { target };
        Log(label, new
        {
            nativeType = kind,
            locator = target,
            Name = await ApiAsync(project, Model + "FunctionBase.Name", receiver),
            VisibleName = await ApiAsync(project, Model + "FunctionBase.VisibleName", receiver),
            IsMainFunction = await ApiAsync(project, Model + "Function.IsMainFunction", receiver),
            IsPlaced = await ApiAsync(project, Model + "Placement.IsPlaced", receiver),
            definition = await DefinitionAsync(project, target, "Function")
        });
    }

    private async Task TemplatesAsync(string stage, string project, JsonElement cable)
    {
        var templates = await CollectionAsync(project, Model + "Function.FunctionTemplates", cable);
        Log(stage + " FunctionTemplates count", templates?.Count);
        if (templates is null)
        {
            return;
        }
        foreach (JsonElement template in templates)
        {
            // Cable templates may be connections. Try the function accessor if the connection accessor is unavailable.
            object? triple = await DefinitionAsync(project, template, "Connection") ?? await DefinitionAsync(project, template, "Function");
            Log(stage + " template", new { template, definition = triple });
        }
    }

    private async Task<object?> DefinitionAsync(string project, JsonElement receiver, string kind)
    {
        object parameters;
        if (receiver.TryGetProperty("digest", out _))
        {
            parameters = new { target = receiver, via = "FunctionDefinition" };
        }
        else
        {
            JsonElement? target = await ApiAsync(project, Model + kind + ".FunctionDefinition", new { target = receiver });
            if (target is null)
            {
                return null;
            }
            parameters = new { target };
        }
        return new
        {
            Category = await ApiAsync(project, Model + "FunctionDefinition.FunctionCategory", parameters),
            Group = await ApiAsync(project, Model + "FunctionDefinition.Group", parameters),
            Id = await ApiAsync(project, Model + "FunctionDefinition.Id", parameters)
        };
    }

    private async Task ClearPointNamesAsync(string project, string page, DrawingLocator cable, HashSet<(int, long)> initialPoints)
    {
        var points = await FindAsync(project, "ConnectionDefinitionPoint", page);
        foreach (JsonElement item in points)
        {
            long oid = item.GetProperty("locator").GetProperty("oid").GetInt64();
            var fresh = await FindAsync(project, "ConnectionDefinitionPoint", page);
            foreach (JsonElement point in fresh.Where(p => p.GetProperty("locator").GetProperty("oid").GetInt64() == oid))
            {
                JsonElement target = point.GetProperty("locator");
                JsonElement? owner = await ApiAsync(project, Model + "ConnectionDefinitionPoint.CableDefinitionLine", new { target });
                bool belongs = owner is { ValueKind: JsonValueKind.Object } line && line.TryGetProperty("oid", out var ownerOid)
                    ? line.GetProperty("type").GetInt32() == cable.Type && ownerOid.GetInt64() == cable.Oid
                    : initialPoints.Contains((target.GetProperty("type").GetInt32(), oid));
                if (!belongs)
                {
                    Log("S4 point outside cable or membership unavailable", target);
                    continue;
                }
                Log("S4 clear Name", new { target, result = await ApiAsync(project, Model + "FunctionBase.Name", new { target, value = "" }) });
            }
        }
    }

    private async Task ObserveNativeCableAsync(string project)
    {
        foreach (JsonElement cable in await FindAsync(project, "Cable"))
        {
            JsonElement target = cable.GetProperty("locator");
            string? page = target.GetProperty("page").GetString();
            if (string.IsNullOrEmpty(page) || !bool.TryParse((await ApiAsync(project, Model + "Function.IsMainFunction", new { target }))?.ToString(), out bool main) || !main)
            {
                continue;
            }
            string tag = cable.GetProperty("name").GetString()!;
            var (read, references) = await host.CallOrErrorAsync("eplan_cable_get", new() { ["project"] = project, ["cable"] = tag });
            if (!read)
            {
                Log("native cable parts unavailable " + tag, references);
                continue;
            }
            if (references.GetProperty("cable").GetProperty("parts").GetArrayLength() > 0)
            {
                await ObserveAsync("native fixture cable", project, page, tag, runCheck: false);
                return;
            }
        }
        Log("native fixture", "No placed cable main function with an article reference was found.");
    }

    private async Task<List<JsonElement>> FindAsync(string project, string kind, string? page = null, string? name = null)
    {
        var items = new List<JsonElement>();
        var args = new Dictionary<string, object?> { ["project"] = project, ["object_type"] = kind, ["limit"] = 200 };
        if (page is not null)
        {
            args["page"] = page;
        }
        if (name is not null)
        {
            args["name_contains"] = name;
        }
        do
        {
            var (read, found) = await host.CallOrErrorAsync("eplan_object_find", args);
            if (!read || found.GetProperty("status").GetString() != "supported")
            {
                Log("object discovery unavailable", new { args, found });
                break;
            }
            items.AddRange(found.GetProperty("items").EnumerateArray());
            args["cursor"] = found.GetProperty("nextCursor").GetString();
        } while (args["cursor"] is not null);
        return items;
    }

    private async Task<List<JsonElement>?> CollectionAsync(string project, string group, JsonElement target)
    {
        var items = new List<JsonElement>();
        string? cursor = null;
        do
        {
            JsonElement? value = await ApiAsync(project, group, new { target }, cursor);
            if (value is not { ValueKind: JsonValueKind.Object } page)
            {
                return null;
            }
            if (page.TryGetProperty("outputs", out var outputs))
            {
                page = outputs.GetProperty("items");
            }
            if (!page.TryGetProperty("items", out var rows))
            {
                Log(group + " unavailable collection", page);
                return null;
            }
            items.AddRange(rows.EnumerateArray());
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        return items;
    }

    private async Task<JsonElement?> ApiAsync(string project, string group, object parameters, string? cursor = null)
    {
        CallToolResult result = await _api.TryAsync(new Progress<ProgressNotificationValue>(), group, project,
            JsonSerializer.SerializeToElement(parameters), false, cursor, TestContext.Current.CancellationToken);
        if (result.IsError == true)
        {
            Log(group + " refused", WriteHost.Text(result));
            return null;
        }
        JsonElement value = result.StructuredContent!.Value.GetProperty("value");
        return value.ValueKind == JsonValueKind.Null ? null : value;
    }

    private async Task WriteAsync(string stage, string tool, Dictionary<string, object?> args)
    {
        var (_, result) = await host.CallOrErrorAsync(tool, args);
        Log(stage + " " + tool, result);
    }

    private static void Log(string label, object? value) =>
        TestContext.Current.TestOutputHelper!.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}
