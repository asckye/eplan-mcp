using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>
/// ADR-0059 tier 2: removing objects of the parts database through eplan_api_try. It WRITES THE PARTS DATABASE EPLAN IS
/// CONFIGURED WITH, so it runs only from <see cref="MasterDataOnCopyTests"/>, which points EPLAN at a copy below scratch
/// first. Each pair adds an object, removes that same object again and counts the collection around both where the
/// catalog has a readable one. Every row goes to the test output, including the pairs that could not run.
/// </summary>
[Trait("Size", "Large")]
public sealed class ApiTryPartsDatabaseTests
{
    private const string Master = "O:Eplan.EplApi.MasterData.";
    private static readonly TimeSpan _budget = TimeSpan.FromMinutes(25);

    /// <summary>Owner type, what is added, the collection that lists it, the name of Remove's argument, Add's argument name.</summary>
    private static readonly (string Owner, string Item, string List, string Argument, string? Name)[] _pairs =
    [
        ("MDPart", "AccessoryPosition", "AccessoryPositions", "accessoryPos", null),
        ("MDPart", "AssemblyPosition", "AssemblyPositions", "assemblyPos", null),
        ("MDPart", "DoorPosition", "DoorPositions", "doorPos", null),
        ("MDPart", "LockedMountingAreaPosition", "LockedMountingAreaPositions", "lockedMountingAreaPos", null),
        ("MDPart", "ModulePosition", "ModulePositions", "modulePos", null),
        ("MDPart", "MountingPanelPosition", "MountingPanelPositions", "mountingPanelPos", null),
        ("MDPart", "PartConstructionPosition", "PartConstructionPositions", "partConstructionPos", null),
        ("MDPart", "SafetyRelatedValuePosition", "SafetyRelatedValuePositions", "safetyRelatedValuePos", null),
        ("MDPart", "SupportBarPosition", "SupportBarPositions", "supportBarPos", null),
        ("MDPartsDatabase", "AccessoryList", "AccessoryLists", "accessorylist", "name"),
        ("MDPartsDatabase", "AccessoryPlacement", "AccessoryPlacements", "accessoryplacement", "name"),
        ("MDPartsDatabase", "ConnectionPointInfo", "ConnectionPointInfos", "connectionPointInfo", "name"),
        ("MDPartsDatabase", "Construction", "Constructions", "construction", "name"),
        ("MDPartsDatabase", "Customer", "Customers", "customer", "shortName"),
        ("MDPartsDatabase", "Manufacturer", "Manufacturers", "manufacturer", "shortName"),
    ];

    /// <summary>The positions of the objects the database pairs create: parent item, position, its collection.</summary>
    private static readonly Dictionary<string, (string Position, string List)> _children = new(StringComparer.Ordinal)
    {
        ["AccessoryList"] = ("AccessoryListPosition", "AccessoryListPositions"),
        ["AccessoryPlacement"] = ("AccessoryPlacementPosition", "AccessoryPlacementPositions"),
        ["ConnectionPointInfo"] = ("ConnectionPointPosition", "ConnectionPointPositions"),
        ["Construction"] = ("DrillingPosition", "DrillingPositions"),
    };

    private ApiTryTools _api = null!;
    private readonly JsonArray _rows = [];

    [Fact]
    public async Task ObjectsAddedToTheCopy_AreRemovedAgain()
    {
        PartImportTests.SkipUnlessOptedIn();
        // One host and the provider below are the only clients: a further one found no EPLAN (live 2026-11-20, as in
        // CablePartFunctionsSpikeTests). The writable scratch project is the anchor every api_try call needs.
        await using var host = new DestructiveHost();
        await host.InitializeAsync();
        string folder = Directory.CreateTempSubdirectory("eplanmcp-mdremove-").FullName;
        var approvals = new TrialApprovals(ApiTryTools.Tool);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new HostDataLocations(folder));
            services.AddSingleton(new HostConfig([LiveEplan.ScratchRoot()], OutsideScratchWrites.Deny) { ApiTry = ActionRunSwitch.On });
            services.AddEplanMcp();
            services.RemoveAll<IApprovalChannel>();
            services.AddSingleton<IApprovalChannel>(approvals);
            await using ServiceProvider provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IBridgeClient>().SelectAsync(host.ProcessId, TestContext.Current.CancellationToken);
            _api = ActivatorUtilities.CreateInstance<ApiTryTools>(provider);
            string project = host.Writable;
            // A service call without object arguments needs the scratch project to be current (live 2026-11-20:
            // POLICY_DENIED otherwise). As PlanStepLiveSession does: close only known fixture copies in front of it.
            var bridge = (BridgeClient)provider.GetRequiredService<IBridgeClient>();
            for (int closed = 0; ; closed++)
            {
                List<OpenProject> open = (await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), TestContext.Current.CancellationToken)).Projects;
                OpenProject current = Assert.Single(open, p => p.Current);
                if (string.Equals(current.Path, project, StringComparison.OrdinalIgnoreCase))
                { break; }
                Assert.True(closed < 5 && (current.Path.StartsWith(Path.Combine(LiveEplan.ScratchRoot(), "fixtures") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(current.Path, Path.Combine(WriteHost.OutsideFolder, "LargeOutside"), StringComparison.OrdinalIgnoreCase)), "The current project is not a fixture copy: " + current.Name);
                await PlanStepLiveSession.ActionAsync("XPrjActionProjectClose");
            }

            JsonElement found = await host.CallAsync("eplan_object_find", new() { ["project"] = project, ["object_type"] = "Project", ["limit"] = 1 });
            JsonElement anchor = found.GetProperty("items")[0].GetProperty("locator");
            JsonElement database = await RequireAsync(project, "MDPartsManagement.OpenDatabase", new { target = anchor });
            JsonElement search = await host.CallAsync("eplan_part_search", new() { ["query"] = "SIE", ["limit"] = 1 });
            JsonElement first = search.GetProperty("parts")[0];
            string number = first.GetProperty("partNumber").GetString()!, variant = first.GetProperty("variant").GetString() ?? "1";
            Log("part", new { number, variant });

            var clock = Stopwatch.StartNew();
            foreach (var pair in _pairs)
            {
                if (clock.Elapsed > _budget)
                {
                    _rows.Add(new JsonObject { ["pair"] = pair.Owner + "." + pair.Item, ["outcome"] = "not run: time budget used" });
                    continue;
                }
                // Handles expire while the backups of the earlier pairs run (live 2026-11-20: PLAN_STALE after about
                // ten minutes), so each pair opens the database again.
                database = await RequireAsync(project, "MDPartsManagement.OpenDatabase", new { target = anchor });
                JsonElement owner = pair.Owner == "MDPart" ? await RequireAsync(project, "MDPartsDatabase.GetPart", new { target = database, partNr = number, variant }) : database;
                await PairAsync(project, pair.Owner, pair.Item, pair.List, pair.Argument, owner,
                    pair.Name is null ? null : new Dictionary<string, object?> { [pair.Name] = "EPLANMCP-" + pair.Item.ToUpperInvariant() });
            }
            Log("approvals asked", approvals.Questions.Count);
            Log("first approval", approvals.Questions.Count == 0 ? null : string.Join(" | ", approvals.Questions[0].Changes));
            Log("elapsed seconds", clock.Elapsed.TotalSeconds);
        }
        finally
        {
            Log("rows", _rows);
            // The wrapper records only "passed" or the exception; the rows are the evidence of a passing run.
            if (Environment.GetEnvironmentVariable("EPLANMCP_MASTERDATA_COPY") is { Length: > 0 } copy)
            { File.WriteAllText(Path.Combine(copy, "apitry-parts-database-rows.json"), _rows.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
            Directory.Delete(folder, recursive: true);
        }

        // A pair whose Add EPLAN refuses for this part (live 2026-11-20: assembly and module positions) says nothing
        // about Remove: it stays unverified in the rows. Whatever was added must be gone again.
        JsonObject[] added = [.. _rows.Select(r => r!.AsObject()).Where(r => (string?)r["outcome"] is { } o && o != "not run: time budget used" && !o.StartsWith("add failed", StringComparison.Ordinal))];
        Assert.True(added.Length >= 10 && added.All(row => (string?)row["outcome"] == "removed"), "rows: " + _rows.ToJsonString());
        Assert.NotEmpty(approvals.Questions);
    }

    /// <summary>Add, remove the same object, count around both; for a parent with positions, the same one level down first.</summary>
    private async Task PairAsync(string project, string ownerType, string item, string list, string argument, JsonElement owner, Dictionary<string, object?>? name)
    {
        var row = new JsonObject { ["pair"] = ownerType + "." + item };
        _rows.Add(row);
        int? before = await CountAsync(project, ownerType + "." + list, owner);
        var add = new Dictionary<string, object?>(name ?? []) { ["target"] = owner };
        var (added, addError) = await CallAsync(project, ownerType + ".Add" + item, add);
        if (added is null)
        {
            row["outcome"] = "add failed: " + addError;
            return;
        }
        int? afterAdd = await CountAsync(project, ownerType + "." + list, owner);
        if (_children.TryGetValue(item, out var child))
        { await PairAsync(project, "MD" + item, child.Position, child.List, "doorPos", added.Value, null); }
        var (_, removeError) = await CallAsync(project, ownerType + ".Remove" + item, new Dictionary<string, object?> { ["target"] = owner, [argument] = added.Value });
        int? afterRemove = await CountAsync(project, ownerType + "." + list, owner);
        row["counts"] = string.Join(" -> ", new[] { before, afterAdd, afterRemove }.Select(c => c?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"));
        row["outcome"] = removeError is not null ? "remove failed: " + removeError
            : before is null || afterAdd is null || afterRemove is null ? "removed"
            : afterAdd == before + 1 && afterRemove == before ? "removed" : "counts do not match";
    }

    /// <summary>The length of a collection property, or null when the catalog has no readable one or it is longer than 2000.</summary>
    private async Task<int?> CountAsync(string project, string property, JsonElement target)
    {
        if (!ApiTryCatalog.Allowed(Master + property))
        { return null; }
        int count = 0;
        string? cursor = null;
        do
        {
            var (value, _) = await CallAsync(project, property, new { target }, cursor);
            if (value is not { ValueKind: JsonValueKind.Object } page)
            { return null; }
            if (page.TryGetProperty("outputs", out JsonElement outputs))
            { page = outputs.GetProperty("items"); }
            if (!page.TryGetProperty("items", out JsonElement items))
            { return null; }
            count += items.GetArrayLength();
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null && count <= 2000);
        return cursor is null ? count : null;
    }

    /// <summary>A call the test cannot go on without; the refusal text reaches the evidence file through the exception.</summary>
    private async Task<JsonElement> RequireAsync(string project, string member, object parameters)
    {
        var (value, error) = await CallAsync(project, member, parameters);
        return value ?? throw new InvalidOperationException(member + " on " + project + " returned no value: " + (error ?? "null") + " | parameters " + JsonSerializer.Serialize(parameters));
    }

    private async Task<(JsonElement? Value, string? Error)> CallAsync(string project, string member, object parameters, string? cursor = null)
    {
        CallToolResult result = await _api.TryAsync(new Progress<ProgressNotificationValue>(), Master + member, project,
            JsonSerializer.SerializeToElement(parameters), false, cursor, TestContext.Current.CancellationToken);
        if (result.IsError == true)
        {
            string text = WriteHost.Text(result);
            Log(member + " refused", text);
            return (null, text);
        }
        JsonElement value = result.StructuredContent!.Value.GetProperty("value");
        return (value.ValueKind == JsonValueKind.Null ? null : value, null);
    }

    private static void Log(string label, object? value) =>
        TestContext.Current.TestOutputHelper!.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}
