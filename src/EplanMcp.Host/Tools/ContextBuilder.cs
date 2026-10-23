using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tools;

/// <summary>Read-only context, fresh snapshot evidence and exact workspace locks (phase 2 design D14/D15).</summary>
internal static class ContextBuilder
{
    internal static readonly string[] Sections = ["objects", "facts", "modules", "rules", "config", "capabilities"];
    internal static readonly string[] Defaults = ["objects", "facts", "modules", "config", "capabilities"];
    private static readonly string[] _gaps = ["snapshot:graphics_text_placeholders", "config:layer_merge", "config:values", "modules:insert_and_value_sets", "rules:execution"];

    public static async Task<JsonObject> ReadAsync(IBridgeClient bridge, PackLibrary packs, string? project, JsonElement scope, string[] include, int tokens, CancellationToken cancellationToken)
    {
        string[]? Strings(string key) => scope.TryGetProperty(key, out JsonElement value) ? value.Deserialize<string[]>() : null;
        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Problem is not null)
        {
            throw new BridgeCallException(status.Problem);
        }

        if (status.Handshake?.Operations?.Contains(OperationNames.ProjectSnapshot, StringComparer.Ordinal) != true)
        {
            throw Missing("op:project.snapshot");
        }

        PlanSnapshot snapshot = await new PlanBaselineReader(bridge).SnapshotAsync(project, scope.TryGetProperty("locators", out _) ? null : Strings("pages"), cancellationToken).ConfigureAwait(false);
        ProjectBinding? binding = await packs.Workspaces.ReadAsync(snapshot.Project.Path, cancellationToken).ConfigureAwait(false);
        if (binding is not null && (!Path.IsPathFullyQualified(binding.Project) || PlanSnapshotHashes.ProjectKey(binding.Project) != snapshot.Project.Path))
        {
            throw Missing("workspace:project_identity");
        }

        if (binding is not null && (binding.Cabinets.Count > 1 || binding.Cabinets.Any(c => c.Id != "default")))
        {
            throw Missing("cabinet:single_default_binding");
        }

        if (binding?.Cabinets.Count == 0)
        {
            binding = null;
        }

        var filter = new PlanScope([.. Strings("pages") ?? snapshot.Pages.Select(p => p.Name)], [.. Strings("structures") ?? []],
            scope.TryGetProperty("locators", out JsonElement locators) ? locators.Deserialize<System.Collections.Immutable.ImmutableArray<PlanLocator>>(HostJson.Options) : []);
        if (filter.Locators.Any(l => !snapshot.Pages.SelectMany(p => p.Objects).Any(o => PlanScopeRules.Same(l, PlanScopeRules.Locator(snapshot.Project.Path, o))))
            || filter.Pages.Except(snapshot.Pages.Select(p => p.Name), StringComparer.Ordinal).Any())
        {
            throw new BridgeCallException(new BridgeError
            {
                Reason = ErrorReason.ResourceNotFound,
                Message = "A scope locator or page is absent or stale. Read a fresh snapshot and use its locators.",
                Metadata = new() { ["scope"] = Clean(JsonNode.Parse(scope.GetRawText())!).ToJsonString(HostJson.Options) },
            });
        }

        List<SnapshotPage> pages = [.. snapshot.Pages.Where(p => filter.Pages.Contains(p.Name, StringComparer.Ordinal)
            && (filter.Structures.IsEmpty || filter.Structures.Any(s => PlanScopeRules.StructurePrefix(p.Name, s)))
            && (filter.Locators.IsEmpty || p.Objects.Any(o => filter.Locators.Any(l => PlanScopeRules.Same(l, PlanScopeRules.Locator(snapshot.Project.Path, o))))))
            .OrderBy(p => p.Name, StringComparer.Ordinal)];
        var result = new JsonObject
        {
            ["project"] = Node(new { snapshot.Project.Name, snapshot.Project.Path }),
            ["cabinet"] = "default",
            ["scope"] = JsonNode.Parse(scope.GetRawText()),
            ["contextRef"] = Reference(scope.EnumerateObject().Any() ? null : snapshot.ComposedHash, binding),
            ["sources"] = new JsonArray(),
            ["coverage"] = Node(new
            {
                snapshot.Profile,
                snapshot.Format,
                Pages = pages.Select(p => new { p.Name, p.Type, p.Oid, p.Hash }),
                Missing = _gaps
                .Concat(binding is null ? ["config:binding"] : []).Concat(snapshot.SkippedConnections > 0 ? ["snapshot:skipped_connections"] : [])
            }),
        };
        var items = Sections.ToDictionary(s => s, _ => new List<(JsonObject Item, JsonObject Source)>(), StringComparer.Ordinal);
        DateTimeOffset extractedAt = packs.Clock.GetUtcNow();
        string[]? kinds = Strings("kinds");
        foreach (SnapshotObject obj in PlanScopeRules.Select(snapshot, filter).Where(o => kinds is null || kinds.Contains(o.Kind, StringComparer.Ordinal))
            .OrderBy(o => o.Kind == "page" ? o.Name : o.Page, StringComparer.Ordinal).ThenBy(o => o.Kind, StringComparer.Ordinal).ThenBy(o => o.Type).ThenBy(o => o.Oid))
        {
            var locator = PlanScopeRules.Locator(snapshot.Project.Path, obj);
            JsonObject source = Node(new
            {
                locator.Project,
                obj.Kind,
                obj.Type,
                obj.Oid,
                obj.Hash,
            });
            source["id"] = "s-" + Digest(source);
            source["locator"] = Node(locator);
            source["format"] = snapshot.Format;
            source["profile"] = snapshot.Profile;
            source["extractedAt"] = JsonValue.Create(extractedAt);
            source["pageHash"] = pages.First(p => p.Name == (obj.Kind == "page" ? obj.Name : obj.Page)).Hash;
            items["objects"].Add((Node(new
            {
                Locator = locator,
                obj.Hash,
                obj.PageType,
                obj.Category,
                obj.Shown,
                obj.Main,
                obj.Pos,
                obj.Parts,
                obj.Start,
                obj.End,
                obj.Values,
            }), source));
            foreach (SnapshotValue value in obj.Values.OrderBy(v => v.Id).ThenBy(v => v.Index))
            {
                items["facts"].Add((Node(new
                {
                    Locator = locator,
                    Category = "fact",
                    Property = value
                }), source));
            }
        }

        foreach (PackReference pin in (binding?.Locks ?? []).OrderBy(l => l.Id, StringComparer.Ordinal).ThenBy(l => l.Version, StringComparer.Ordinal))
        {
            if (!include.Any(s => s is "modules" or "rules"))
            {
                break;
            }

            if (packs.Installed.InstalledDigest(pin.Id, pin.Version) != pin.Digest)
            {
                throw Missing("pack:exact_locked_version");
            }

            string folder = packs.Installed.FolderOf(pin.Id, pin.Version);
            (PackManifest? manifest, string? digest, IReadOnlyList<PackProblem> problems) = await PackVerifier.ReadManifestAsync(folder, cancellationToken).ConfigureAwait(false);
            if (manifest is null || problems.Count > 0 || digest != pin.Digest || manifest.Id != pin.Id || manifest.Version != pin.Version)
            {
                throw Missing("pack:valid_locked_manifest");
            }

            foreach (AssetEntry entry in (await AssetIndex.ReadAsync(folder, manifest, cancellationToken).ConfigureAwait(false)).OrderBy(e => e.Resource.Path, StringComparer.Ordinal))
            {
                string? section = entry.Kind is "macro" or "module" ? "modules" : entry.Kind is "rule" or "candidate" or "fact_set" ? "rules" : null;
                if (section is null || !include.Contains(section, StringComparer.Ordinal))
                {
                    continue;
                }

                string? problem = await PackVerifier.ResourceProblemAsync(folder, entry.Resource, packs.Config.PackSources, cancellationToken).ConfigureAwait(false) ?? entry.DocumentProblem;
                JsonObject source = Node(new
                {
                    Pack = pin.Id,
                    ExactVersion = pin.Version,
                    ResourcePath = entry.Resource.Path,
                    ManifestDigest = digest,
                    FileHash = entry.Resource.Sha256,
                });
                source["id"] = "s-" + Digest(source);
                source["origin"] = Node(new
                {
                    entry.Document?.Origin?.Asset,
                    entry.Document?.Origin?.Fact,
                    entry.Document?.Origin?.Page
                });
                items[section].Add((Node(new
                {
                    Asset = entry.Id,
                    Category = entry.Kind,
                    Standing = entry.Standing is { } standing ? PackTools.Snake(standing) : null,
                    Title = entry.Document?.Metadata.Title,
                    Hash = entry.Resource.Sha256,
                    Available = problem is null,
                    Problem = problem,
                    Missing = section == "modules" ? "module.insert/placeholder.apply" : "rules:execution",
                }), source));
            }
        }

        if (include.Contains("capabilities", StringComparer.Ordinal))
        {
            foreach (string operation in new[] { OperationNames.ProjectSnapshot, "module.insert", "rules.execute" }.Order(StringComparer.Ordinal))
            {
                items["capabilities"].Add((Node(new
                {
                    Capability = operation,
                    Available = operation == OperationNames.ProjectSnapshot
                }), new JsonObject()));
            }
        }

        var missing = result["coverage"]!["missing"]!.AsArray();
        foreach (string section in Sections.Where(s => include.Contains(s, StringComparer.Ordinal) && items[s].Count == 0))
        {
            missing.Add(section + ":no_material");
        }

        return Fit(Clean(result).AsObject(), items, include, tokens);
    }

    internal static JsonObject Fit(JsonObject result, Dictionary<string, List<(JsonObject Item, JsonObject Source)>> items, string[] include, int tokens)
    {
        int total = include.Sum(s => items[s].Count);
        int kept = 0;
        foreach (string section in Sections.Where(s => include.Contains(s, StringComparer.Ordinal)))
        {
            result[section] = new JsonArray();
        }

        result["budget"] = Node(new
        {
            RequestedTokens = tokens,
            EstimatedTokens = 0,
            OmittedItems = total
        });

        int Measure()
        {
            result["budget"]!["omittedItems"] = total - kept;
            result["truncated"] = kept < total;
            result["hint"] = kept < total ? "Narrow scope/include or increase max_tokens to retrieve omitted items." : null;
            int estimate;
            int next;
            do
            {
                estimate = result["budget"]!["estimatedTokens"]!.GetValue<int>();
                next = ResultShaper.ResultTokens(result.ToJsonString(HostJson.Options)) + ResultShaper.EstimateTokens(Summary(result));
                result["budget"]!["estimatedTokens"] = next;
            } while (estimate != next);

            return next;
        }

        int minimum = Measure();
        if (minimum > tokens)
        {
            do
            {
                result["budget"]!["requestedTokens"] = minimum;
                minimum = Measure();
            } while (result["budget"]!["requestedTokens"]!.GetValue<int>() != minimum);

            throw new BridgeCallException(new BridgeError
            {
                Reason = ErrorReason.InvalidArgument,
                Message = "The necessary context envelope exceeds max_tokens. Narrow scope or increase the budget.",
                Metadata = new()
                {
                    ["violations"] = "/max_tokens: envelope does not fit",
                    ["minimumEstimatedTokens"] = minimum.ToString(CultureInfo.InvariantCulture),
                },
            });
        }

        foreach (string section in Sections.Where(s => include.Contains(s, StringComparer.Ordinal)))
        {
            foreach ((JsonObject item, JsonObject source) in items[section])
            {
                string? id = source["id"]?.GetValue<string>();
                bool added = id is not null && !result["sources"]!.AsArray().Any(s => s!["id"]!.GetValue<string>() == id);
                item["sources"] = id is null ? new JsonArray() : new JsonArray(id);
                result[section]!.AsArray().Add(Clean(item));
                if (added)
                {
                    result["sources"]!.AsArray().Add(Clean(source));
                }

                kept++;
                if (Measure() <= tokens)
                {
                    continue;
                }

                result[section]!.AsArray().RemoveAt(result[section]!.AsArray().Count - 1);
                if (added)
                {
                    result["sources"]!.AsArray().RemoveAt(result["sources"]!.AsArray().Count - 1);
                }

                kept--;
                _ = Measure();
                return result;
            }
        }

        _ = Measure();
        return result;
    }

    internal static string Summary(JsonObject result) => string.Create(CultureInfo.InvariantCulture,
        $"Returned context for {ToolResults.Count(result["coverage"]!["pages"]!.AsArray().Count, "page")}; {result["budget"]!["omittedItems"]!.GetValue<int>()} items omitted by the token budget.");

    internal static JsonObject Node<T>(T value) => JsonSerializer.SerializeToNode(value, HostJson.Options)!.AsObject();

    internal static JsonObject Reference(string? snapshotHash, ProjectBinding? binding) => Node(new
    {
        SnapshotHash = snapshotHash,
        Generation = (string?)null,
        BindingRevision = binding?.Revision,
        LockDigest = binding is null ? null : Digest(JsonSerializer.SerializeToNode(binding.Locks.OrderBy(l => l.Id, StringComparer.Ordinal).ThenBy(l => l.Version, StringComparer.Ordinal).ThenBy(l => l.Digest, StringComparer.Ordinal), HostJson.Options)!),
    });

    private static string Digest(JsonNode value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(PlanCanonical.Serialize(JsonSerializer.SerializeToElement(value, HostJson.Options)))));

    private static JsonNode Clean(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : Clean(p.Value)))),
        JsonArray array => new JsonArray([.. array.Select(v => v is null ? null : Clean(v))]),
        JsonValue value when value.TryGetValue(out string? text) => JsonValue.Create(ResultShaper.CleanText(text))!,
        _ => node.DeepClone(),
    };

    internal static BridgeCallException Missing(string capability) => new(new BridgeError
    {
        Reason = ErrorReason.CapabilityMissing,
        Message = "This context needs an unavailable capability. Use the default cabinet with existing exact locks and a compatible Bridge.",
        Metadata = new() { ["missing"] = capability },
    });
}
