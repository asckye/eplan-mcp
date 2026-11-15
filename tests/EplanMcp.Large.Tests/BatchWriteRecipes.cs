using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Write arguments the batch cannot discover (fixtures/batch-write-recipes.json): for a write group, a value per parameter
/// name, $project for the batch project's locator or a literal such as a part number of the development machine's parts
/// database. An entry whose parameters a recipe covers is trialled instead of being recorded as needing context; its
/// receiver and the undo check stay as for every write.
/// </summary>
internal sealed class BatchWriteRecipes
{
    public const string Project = "$project";

    private readonly Dictionary<string, Dictionary<string, JsonElement>> _recipes;

    private BatchWriteRecipes(Dictionary<string, Dictionary<string, JsonElement>> recipes) => _recipes = recipes;

    internal static BatchWriteRecipes Default { get; } = Load();

    internal static BatchWriteRecipes Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var recipes = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        foreach (JsonProperty recipe in document.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith('_')))
        {
            if (!recipe.Name.EndsWith("#write", StringComparison.Ordinal) || recipe.Value.ValueKind != JsonValueKind.Object)
            { throw new FormatException("A write recipe is an object under a write entry ID: " + recipe.Name); }
            recipes[recipe.Name] = recipe.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }
        return new(recipes);
    }

    private static BatchWriteRecipes Load()
    {
        using var reader = new StreamReader(typeof(BatchWriteRecipes).Assembly.GetManifestResourceStream("BatchWriteRecipes.json")!);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>The recipe for <paramref name="id"/> when it gives a value for every one of <paramref name="parameters"/>.</summary>
    internal Dictionary<string, JsonElement>? For(string id, IEnumerable<string>? parameters) =>
        _recipes.TryGetValue(id, out Dictionary<string, JsonElement>? recipe) && (parameters ?? []).All(recipe.ContainsKey) ? recipe : null;
}
