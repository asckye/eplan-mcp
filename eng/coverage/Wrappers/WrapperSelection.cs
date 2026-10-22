using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EplanMcp.Coverage.Wrappers;

/// <summary>A catalog type to generate for: properties to copy into a DTO and method groups to wrap, by simple name.</summary>
internal sealed class TypeSelection
{
    /// <summary>The type's doc-comment ID, <c>T:…</c>.</summary>
    public string Type { get; set; } = "";

    public List<string> Properties { get; set; } = [];

    public List<string> Overloads { get; set; } = [];
}

/// <summary>An action row to generate a parameter class for, and the class's name.</summary>
internal sealed class ActionSelection
{
    /// <summary>The row's ID in the action catalog, <c>A:action/TYPE</c> or <c>A:action</c>.</summary>
    public string Action { get; set; } = "";

    public string Class { get; set; } = "";
}

/// <summary>
/// catalog/wrappers.yaml: which catalog entries <see cref="WrapperGenerator"/> writes building blocks for. Kept short on
/// purpose, so that the generated code stays reviewable. Unknown fields are errors, so a typo cannot drop an entry.
/// </summary>
internal sealed class WrapperSelection
{
    public const string RelativePath = "catalog/wrappers.yaml";

    /// <summary>The catalog folder under catalog/, for example <c>eplan-2026.0.3</c>.</summary>
    public string Catalog { get; set; } = "";

    public List<TypeSelection> Types { get; set; } = [];

    public List<ActionSelection> Actions { get; set; } = [];

    /// <summary>The selection, or null when the repository has none.</summary>
    public static WrapperSelection? Load(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, RelativePath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            WrapperSelection selection = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build()
                .Deserialize<WrapperSelection?>(File.ReadAllText(path)) ?? new WrapperSelection();
            return selection.Catalog.Length > 0 && !selection.Catalog.Contains("..", StringComparison.Ordinal) && selection.Catalog.IndexOfAny(['/', '\\']) < 0
                ? selection
                : throw new InvalidDataException($"{RelativePath}: 'catalog' must name a folder under catalog/, such as eplan-2026.0.3.");
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException($"{RelativePath}: {ex.Message}", ex);
        }
    }
}
