using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Packs;

namespace EplanMcp.Large.Tests;

/// <summary>Read-only comparison with native sample structure properties; unconfirmed until run against LiveEplan.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class CabinetScopeTests(LiveEplan eplan)
{
    private static readonly (int Id, string Marker)[] _properties = [(1140, "="), (1240, "+"), (1540, "&"), (1340, "=="), (1740, "++"), (1640, "#")];

    [Fact]
    public async Task CabinetOf_SampleDesignations_MatchesNativeStructureProperties()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        var resolver = new CabinetScopeResolver([Cabinet("a1", "+A1"), Cabinet("a2", "+A2"), Cabinet("a10", "+A10")]);
        int proven = 0, unproven = 0;
        var locations = new HashSet<string>(StringComparer.Ordinal);

        foreach (SnapshotPage page in sample.Snapshot.Pages)
        {
            await CompareAsync(page.Name, new PropertyTarget(page: page.Name));
        }

        // Complete plan.functions evidence includes page-less functions, independent of page membership.
        foreach (PlanFunction function in sample.Functions.GroupBy(f => f.Name, StringComparer.Ordinal)
            .Select(g => g.FirstOrDefault(f => f.Page is null) ?? g.First()))
        {
            await CompareAsync(function.Name, new PropertyTarget(locator: new ObjectLocator
            {
                Project = eplan.ProjectPath,
                Kind = function.Kind,
                Type = function.Type,
                Oid = function.Oid,
                Name = function.Name,
                Page = function.Page,
            }));
        }

        Assert.Contains("+A1", locations);
        Assert.Contains("+A2", locations);
        Assert.Equal(["a1"], resolver.CabinetOf("=GAA+A1-FC1"));
        Assert.True(proven >= sample.Snapshot.Pages.Count);
        TestContext.Current.TestOutputHelper?.WriteLine($"Native structure comparison: {proven} proven, {unproven} denied; {sample.Functions.Count(f => f.Page is null)} page-less functions inventoried.");

        async Task CompareAsync(string designation, PropertyTarget target)
        {
            List<PropertyReadRow> rows = await LiveProperties.ReadAsync(bridge, LivePropertyObjects.Args(eplan, target, [.. _properties.Select(p => p.Id)]));
            Assert.Equal(_properties.Select(p => p.Id), rows.Select(r => r.PropertyId));
            Assert.All(rows, r => Assert.True(r.Status is "value" or "empty" or "not_applicable", $"Structure property {r.PropertyId}: {r.Status}."));
            IReadOnlyDictionary<string, string>? segments = CabinetScopeResolver.ParseDesignation(designation);
            if (segments is null)
            {
                Assert.Empty(resolver.CabinetOf(designation));
                unproven++;
                return;
            }

            proven++;
            for (int i = 0; i < rows.Count; i++)
            {
                string? expected = rows[i].Status == "value" ? rows[i].Value?.Text ?? rows[i].Display : null;
                Assert.Equal(string.IsNullOrEmpty(expected) ? null : expected, segments.GetValueOrDefault(_properties[i].Marker));
            }

            string? location = rows[1].Status == "value" ? rows[1].Value?.Text ?? rows[1].Display : null;
            if (!string.IsNullOrEmpty(location))
            {
                locations.Add(location);
            }

            string? cabinet = location switch { "+A1" => "a1", "+A2" => "a2", "+A10" => "a10", _ => null };
            Assert.Equal(cabinet is null ? [] : new[] { cabinet }, resolver.CabinetOf(designation));
        }
    }

    private static CabinetBinding Cabinet(string id, string location) => new()
    {
        Id = id,
        Layers = [],
        Values = [],
        Structure = new JsonObject { ["prefixes"] = new JsonArray(new JsonObject { ["location"] = location }) },
    };
}
