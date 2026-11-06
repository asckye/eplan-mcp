using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Host.Tools;

internal sealed record ReviewRule(RuleDefinition Definition, JsonObject Spec);
internal sealed record ReviewRulePack(PluginDescriptor Plugin, IReadOnlyList<ReviewRule> Rules, IReadOnlyList<string> Warnings);

/// <summary>Review consumes installed rule data without the engineering workflow's standing or cabinet gates.</summary>
internal static class ReviewRulePacks
{
    internal static async Task<IReadOnlyList<ReviewRulePack>> LoadAsync(PackLibrary packs, IReadOnlyList<PluginDescriptor> plugins, CancellationToken cancellationToken)
    {
        var loaded = new List<ReviewRulePack>();
        foreach (PluginDescriptor plugin in plugins)
        {
            var rules = new List<ReviewRule>();
            var warnings = new List<string>(plugin.Warnings);
            try
            {
                string folder = new PlanPaths(packs.Locations.Root).Check(packs.Installed.FolderOf(plugin.Id, plugin.Version));
                if (plugin.Manifest is not { } manifest)
                { throw new PackFormatException("Installed manifest is unavailable."); }
                foreach (AssetEntry entry in await AssetIndex.ReadAsync(folder, manifest, cancellationToken).ConfigureAwait(false))
                {
                    if (entry.Resource.Kind != ResourceKind.Rule)
                    { continue; }
                    try
                    {
                        _ = new PlanPaths(packs.Locations.Root).Check(Path.Combine(folder, entry.Resource.Path.Replace('/', Path.DirectorySeparatorChar)));
                        if (entry.Document is not { Kind: KnowledgeKind.Rule, ApiVersion: KnowledgeDocument.V2ApiVersion } document)
                        {
                            if (entry.DocumentProblem is not null)
                            { warnings.Add($"{entry.Id}: rule document is missing, unreadable or invalid."); }
                            continue;
                        }
                        if (await PackVerifier.ResourceProblemAsync(folder, entry.Resource, packs.Config.PackSources, cancellationToken).ConfigureAwait(false) is not null)
                        { throw new PackFormatException("Rule content is unavailable or changed."); }
                        JsonElement spec = JsonSerializer.SerializeToElement(document.Spec, PackJson.Options);
                        (RuleNode scope, RuleNode check) = RuleValidator.Validate(spec);
                        rules.Add(new(new(document.Metadata.Name, plugin.Version, spec.GetProperty("scope").GetProperty("kind").GetString()!, scope, check), document.Spec.DeepClone().AsObject()));
                    }
                    catch (Exception ex) when (PluginConfigurationReader.Unreadable(ex))
                    { warnings.Add($"{entry.Id}: rule content is missing, unreadable or invalid."); }
                }
            }
            catch (Exception ex) when (PluginConfigurationReader.Unreadable(ex))
            { warnings.Add($"{plugin.Id}@{plugin.Version}: installed rules are missing, unreadable or invalid."); }
            loaded.Add(new(plugin, rules, warnings));
        }
        return loaded;
    }

    internal static Task<IReadOnlyList<RuleResult>> EvaluateAsync(ReviewRulePack pack, PageReviewFacts facts, CancellationToken cancellationToken) =>
        new RuleInterpreter().EvaluateAsync([.. pack.Rules.Select(rule => rule.Definition)], facts,
            new Dictionary<string, RuleValue>(), new Dictionary<string, RuleValue>(), cancellationToken);
}
