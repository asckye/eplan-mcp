using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tools;

internal static class PageReviewPlugins
{
    internal sealed record Selection(string ProjectPath, IReadOnlyList<ReviewRulePack> Packs, IReadOnlyList<string> Warnings);

    internal static async Task<Selection> SelectAsync(IBridgeClient bridge, PackLibrary? packs, string project,
        string page, string[]? ids, CancellationToken token)
    {
        if (packs is null)
        {
            if (ids is not null)
            { throw new BridgeCallException(ToolResults.InvalidArgumentError(["/rule_packs: No installed plugins contain rules."])); }
            return new(project, [], []);
        }
        string path = Path.IsPathFullyQualified(project) ? project
            : (await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet, new() { Project = project }, token).ConfigureAwait(false)).Project.Path;
        PluginResolution resolution = await new PluginResolver(packs).ResolveAsync(path, token, page: page).ConfigureAwait(false);
        IReadOnlyList<PluginDescriptor> selected = resolution.Enabled.GroupBy(p => p.Id, StringComparer.Ordinal).Select(g => g.Last()).ToArray();
        if (ids is not null)
        {
            var installed = new List<PluginDescriptor>();
            try
            {
                foreach (string id in packs.Installed.Ids())
                {
                    foreach (SemanticVersion version in packs.Installed.Versions(id))
                    {
                        var read = await PackVerifier.ReadManifestAsync(packs.Installed.FolderOf(id, version.ToString()), token).ConfigureAwait(false);
                        if (read.Problems.Count > 0)
                        { installed.Add(new(id, version.ToString(), "explicit", null, [$"{id}@{version}: installed manifest is unreadable or invalid."])); }
                        else if (read.Manifest is { } manifest && manifest.Resources.Any(r => r.Kind == ResourceKind.Rule))
                        { installed.Add(new(id, version.ToString(), "explicit", manifest, [])); }
                    }
                }
            }
            catch (Exception ex) when (PluginConfigurationReader.Unreadable(ex))
            { return new(path, [], ["The installed plugin directory is unreadable; rule review is unavailable."]); }
            IReadOnlyList<ReviewRulePack> available = await ReviewRulePacks.LoadAsync(packs, installed, token).ConfigureAwait(false);
            installed = available.Where(p => p.Rules.Count > 0 || p.Warnings.Count > 0).Select(p => p.Plugin)
                .GroupBy(p => p.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
            string[] missing = ids.Where(id => !installed.Any(p => p.Id == id)).ToArray();
            if (missing.Length > 0)
            { throw new BridgeCallException(ToolResults.InvalidArgumentError([$"/rule_packs: Not installed or contains no rules: {string.Join(", ", missing)}. Installed plugins with rules: {string.Join(", ", installed.Select(p => p.Id))}."])); }
            selected = ids.Select(id => selected.LastOrDefault(p => p.Id == id && available.Any(a => a.Plugin.Id == p.Id && a.Plugin.Version == p.Version && (a.Rules.Count > 0 || a.Warnings.Count > 0)))
                ?? installed.First(p => p.Id == id)).ToArray();
        }
        IReadOnlyList<ReviewRulePack> loaded = await ReviewRulePacks.LoadAsync(packs,
            selected.Where(p => p.Manifest is null || p.Manifest.Resources.Any(r => r.Kind == ResourceKind.Rule)).ToArray(), token).ConfigureAwait(false);
        ReviewRulePack[] used = loaded.Where(p => p.Rules.Count > 0).ToArray();
        if (used.Any(p => p.Plugin.Manifest?.Signoff?.Status != "signed") && !ScratchPaths.IsInside(path, packs.Config.ScratchRoots))
        { throw new BridgeCallException(ToolResults.InvalidArgumentError(["/rule_packs: Draft plugins can review scratch projects only; an engineer must sign off the plugin before use on other projects."])); }
        return new(path, used, [.. used.Length > 0 ? resolution.Warnings : [], .. loaded.SelectMany(p => p.Warnings)]);
    }

    internal static async Task ApplyAsync(JsonObject data, Selection selection, string page, CancellationToken token)
    {
        var summaries = new JsonArray();
        var facts = new PageReviewFacts(data["actual"]!.AsObject(), new JsonObject
        { ["name"] = page, ["locator"] = new JsonObject { ["project"] = selection.ProjectPath, ["page"] = page } });
        foreach (ReviewRulePack pack in selection.Packs)
        {
            IReadOnlyList<RuleResult> results = await ReviewRulePacks.EvaluateAsync(pack, facts, token).ConfigureAwait(false);
            foreach (ReviewRule rule in pack.Rules)
            { PageReviewRuleIssues.Add(data["issues"]!.AsArray(), pack.Plugin.Id, rule, results.Where(r => r.Rule == rule.Definition.Rule).ToArray(), facts, page); }
            int Count(RuleStatus status) => results.Count(r => r.Status == status);
            summaries.Add(new JsonObject
            {
                ["id"] = pack.Plugin.Id,
                ["version"] = pack.Plugin.Version,
                ["status"] = pack.Plugin.Manifest?.Signoff?.Status ?? "draft",
                ["standards"] = new JsonArray((pack.Plugin.Manifest?.Standards ?? []).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                ["rules"] = pack.Rules.Count,
                ["passed"] = Count(RuleStatus.Pass),
                ["failed"] = Count(RuleStatus.Fail),
                ["unknown"] = Count(RuleStatus.Unknown),
                ["notApplicable"] = Count(RuleStatus.NotApplicable),
            });
        }
        if (summaries.Count > 0)
        { data["rulePacks"] = summaries; }
        if (summaries.Count > 0 || selection.Warnings.Count > 0)
        {
            var metadata = data["metadata"] as JsonObject ?? new JsonObject();
            if (summaries.Count > 0)
            { metadata["plugins"] = new JsonArray(selection.Packs.Select(p => (JsonNode?)JsonValue.Create($"{p.Plugin.Id}@{p.Plugin.Version}")).ToArray()); }
            if (selection.Warnings.Count > 0)
            {
                var warnings = new JsonArray(selection.Warnings.Take(20).Select(w => (JsonNode?)JsonValue.Create(w.Length > 300 ? w[..300] + "..." : w)).ToArray());
                if (selection.Warnings.Count > 20)
                { warnings.Add($"{selection.Warnings.Count - 20} more plugin read warnings; inspect the installed rule resources."); }
                metadata["warnings"] = warnings;
            }
            data["metadata"] = metadata;
        }
    }
}
