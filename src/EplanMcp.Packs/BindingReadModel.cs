using System.Text.Json;

namespace EplanMcp.Packs;

/// <summary>Strict binding shape and potential cabinet intersections; configuration resolution stays separate.</summary>
internal static class BindingReadModel
{
    internal static void Validate(JsonElement root)
    {
        bool v2 = root.GetProperty("bindingVersion").GetInt32() == 2;
        Object(root, "", "bindingVersion project revision", v2 ? "cabinets locks projectValues packRoots" : "cabinets locks");
        if (!v2)
        {
            Each(root, "cabinets", "", (cabinet, at) => Object(cabinet, at, "id", "title"));
            return;
        }

        FiniteNumbers(root, "");
        Require(Text(root.GetProperty("project")), "/project", "must be a project key");
        Require(root.GetProperty("revision").ValueKind == JsonValueKind.Number && root.GetProperty("revision").TryGetInt32(out int revision) && revision >= 0, "/revision", "must be a nonnegative integer");
        Require(root.TryGetProperty("projectValues", out JsonElement values), "/projectValues", "is required");
        Settings(values, "/projectValues");
        Each(root, "packRoots", "", (pin, at) => Pin(pin, at, false), required: true);
        Each(root, "locks", "", (pin, at) => Pin(pin, at, true), required: true);
        Each(root, "cabinets", "", (cabinet, at) =>
        {
            Object(cabinet, at, "id layers values structure", "title");
            Require(Text(cabinet.GetProperty("id")) && PackManifestValidator.IsId(cabinet.GetProperty("id").GetString()!), at + "/id", "must be a cabinet id");
            Require(!cabinet.TryGetProperty("title", out JsonElement title) || Text(title), at + "/title", "must be text");
            Each(cabinet, "layers", at, (layer, pointer) =>
                Require(Text(layer) && AssetNames.TryParse(layer.GetString(), out string? id, out string? version, out string? local)
                    && PackManifestValidator.IsId(id) && SemanticVersion.TryParse(version, out _) && local.StartsWith("config_layer/", StringComparison.Ordinal)
                    && PackManifestValidator.IsId(local[13..]), pointer, "must name an exact pack@version:config_layer/name"), required: true);
            Settings(cabinet.GetProperty("values"), at + "/values");
            JsonElement structure = cabinet.GetProperty("structure");
            Object(structure, at + "/structure", "", "all prefixes");
            bool all = structure.TryGetProperty("all", out JsonElement flag);
            Require(all ? flag.ValueKind == JsonValueKind.True && !structure.TryGetProperty("prefixes", out _)
                && root.GetProperty("cabinets").GetArrayLength() == 1 && cabinet.GetProperty("id").GetString() == "default"
                : structure.TryGetProperty("prefixes", out JsonElement prefixes) && prefixes.ValueKind == JsonValueKind.Array && prefixes.GetArrayLength() > 0,
                at + "/structure", "must be {all:true} for the sole default cabinet, or nonempty {prefixes:[...]}");
            Each(structure, "prefixes", at + "/structure", (prefix, pointer) =>
            {
                Object(prefix, pointer, "", "installation location documentType functionalAssignment installationNumber userDefined");
                Require(prefix.EnumerateObject().Any(), pointer, "must contain a full structure segment");
                foreach (JsonProperty segment in prefix.EnumerateObject())
                {
                    string marker = CabinetScopeResolver.Marker(segment.Name);
                    string text = Text(segment.Value) ? segment.Value.GetString()! : string.Empty;
                    Require(text.StartsWith(marker, StringComparison.Ordinal) && text.Length > marker.Length
                        && !text[marker.Length..].Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "=+&#/*?[](){}|\\^$".Contains(c, StringComparison.Ordinal)),
                        Child(pointer, segment.Name), "must be one complete literal structure segment; wildcards and regex are forbidden");
                }
            });
        }, required: true);
        CabinetBinding[] cabinets = root.GetProperty("cabinets").Deserialize<CabinetBinding[]>(PackJson.Options)!;
        for (int i = 0; i < cabinets.Length; i++)
        {
            for (int j = i + 1; j < cabinets.Length; j++)
            {
                Require(!CabinetScopeResolver.Overlaps(cabinets[i], cabinets[j]), $"/cabinets/{j}/structure",
                    $"overlaps /cabinets/{i}/structure; use disjoint literal structure prefixes");
            }
        }
    }

    private static void Pin(JsonElement pin, string at, bool digest)
    {
        Object(pin, at, digest ? "id version digest" : "id version", "");
        Require(Text(pin.GetProperty("id")) && PackManifestValidator.IsId(pin.GetProperty("id").GetString()!), at + "/id", "must be a pack id");
        Require(Text(pin.GetProperty("version")) && SemanticVersion.TryParse(pin.GetProperty("version").GetString(), out _), at + "/version", "must be an exact SemVer version");
        Require(!digest || (Text(pin.GetProperty("digest")) && PackManifestValidator.IsSha256(pin.GetProperty("digest").GetString()!)), at + "/digest", "must be a SHA-256");
    }

    internal static void Settings(JsonElement values, string at)
    {
        Require(values.ValueKind == JsonValueKind.Object, at, "must be settings keyed by configuration name");
        foreach (JsonProperty value in values.EnumerateObject())
        {
            string pointer = Child(at, value.Name);
            Object(value.Value, pointer, "", "default constraint");
            Require(!value.Value.TryGetProperty("default", out JsonElement fallback) || Scalar(fallback), pointer + "/default", "must be text, number or boolean");
            if (value.Value.TryGetProperty("constraint", out JsonElement constraint))
            {
                string path = pointer + "/constraint";
                Object(constraint, path, "", "fixed allowed");
                Require(constraint.EnumerateObject().Count() == 1, path, "must contain exactly fixed or allowed");
                JsonProperty choice = constraint.EnumerateObject().Single();
                Require(choice.Name == "fixed" ? Scalar(choice.Value)
                    : choice.Value.ValueKind == JsonValueKind.Array ? choice.Value.GetArrayLength() > 0 && choice.Value.EnumerateArray().All(Scalar)
                    : choice.Value.ValueKind == JsonValueKind.Object, Child(path, choice.Name), "must be a scalar, an allowed set or a closed interval");
                if (choice.Name == "allowed" && choice.Value.ValueKind == JsonValueKind.Object)
                {
                    Object(choice.Value, path + "/allowed", "min max", "");
                    Require(choice.Value.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.Number), path + "/allowed", "interval bounds must be numbers");
                }
            }
        }
    }

    internal static void FiniteNumbers(JsonElement node, string at)
    {
        Require(node.ValueKind != JsonValueKind.Number || node.TryGetDecimal(out _), at, "must be a finite decimal");
        IEnumerable<(JsonElement Value, string Pointer)> children = node.ValueKind switch
        {
            JsonValueKind.Object => node.EnumerateObject().Select(p => (p.Value, Child(at, p.Name))),
            JsonValueKind.Array => node.EnumerateArray().Select((item, index) => (item, $"{at}/{index}")),
            _ => [],
        };
        foreach ((JsonElement value, string pointer) in children)
        {
            FiniteNumbers(value, pointer);
        }
    }

    internal static void Object(JsonElement node, string at, string required, string optional)
    {
        Require(node.ValueKind == JsonValueKind.Object, at, "must be an object");
        string[] names = required.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string name in names)
        {
            Require(node.TryGetProperty(name, out _), Child(at, name), "is required");
        }

        foreach (JsonProperty member in node.EnumerateObject())
        {
            Require(names.Contains(member.Name, StringComparer.Ordinal) || optional.Split(' ').Contains(member.Name, StringComparer.Ordinal), Child(at, member.Name), "is not a member here");
        }
    }

    private static void Each(JsonElement parent, string name, string at, Action<JsonElement, string> check, bool required = false)
    {
        string pointer = Child(at, name);
        if (!parent.TryGetProperty(name, out JsonElement node))
        {
            Require(!required, pointer, "is required");
            return;
        }

        Require(node.ValueKind == JsonValueKind.Array, pointer, "must be an array");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((JsonElement item, int index) in node.EnumerateArray().Select((item, index) => (item, index)))
        {
            check(item, $"{pointer}/{index}");
            if (required && (name is "locks" or "packRoots" or "cabinets"))
            {
                Require(seen.Add(item.GetProperty("id").GetString()!), $"{pointer}/{index}/id", "must be unique; a project uses one version per pack id");
            }
        }
    }

    private static bool Text(JsonElement node) => node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(node.GetString());

    private static bool Scalar(JsonElement node) => node.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    internal static string Child(string at, string name) => at + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    internal static void Require(bool valid, string at, string message)
    {
        if (!valid)
        {
            throw new PackFormatException($"{at}: {message}");
        }
    }
}
