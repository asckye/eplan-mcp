using System.Text.Json;

namespace EplanMcp.Packs;

public sealed record MaterialMappingDefinition(JsonElement Spec, JsonElement ParameterSchema)
{
    public JsonElement Select(JsonElement values)
    {
        ModuleValidator.Values(ParameterSchema, values, "/values");
        JsonElement[] matches = [.. Spec.GetProperty("entries").EnumerateArray().Where(e => e.GetProperty("when").EnumerateObject().All(p =>
            (values.TryGetProperty(p.Name, out JsonElement value) || ParameterSchema.GetProperty("items").GetProperty(p.Name).TryGetProperty("default", out value)) && JsonElement.DeepEquals(p.Value, value)))];
        BindingReadModel.Require(matches.Length == 1, "/spec/entries", matches.Length == 0 ? "RESOURCE_NOT_FOUND: no exact part mapping" : "CONFIG_CONFLICT: ambiguous part mapping");
        return matches[0].Clone();
    }
}
