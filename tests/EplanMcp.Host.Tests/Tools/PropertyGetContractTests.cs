using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PropertyGetContractTests
{
    [Fact]
    public void Input_ClosedObjectsDefaultsAndPublishedBounds_AreExplicit()
    {
        JsonElement input = PropertyGetContract.Input;
        Assert.Equal(["queries"], Strings(input.GetProperty("required")));
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        JsonElement fields = input.GetProperty("properties");
        Assert.Equal(50, fields.GetProperty("limit").GetProperty("default").GetInt32());
        Assert.Equal(200, fields.GetProperty("limit").GetProperty("maximum").GetInt32());
        Assert.Equal("concise", fields.GetProperty("response_format").GetProperty("default").GetString());
        Assert.Equal(["concise", "detailed"], Strings(fields.GetProperty("response_format").GetProperty("enum")));
        Assert.Equal(50, fields.GetProperty("queries").GetProperty("maxItems").GetInt32());
        Assert.Equal(20, fields.GetProperty("queries").GetProperty("items").GetProperty("properties").GetProperty("properties").GetProperty("maxItems").GetInt32());
        JsonElement definitions = input.GetProperty("$defs");
        Assert.Equal(2, definitions.GetProperty("selector").GetProperty("oneOf").GetArrayLength());
        Assert.Equal(4, definitions.GetProperty("target").GetProperty("oneOf").GetArrayLength());
        Assert.Equal(["page", "function", "connection"], Strings(definitions.GetProperty("locator").GetProperty("properties").GetProperty("kind").GetProperty("enum")));
        CheckReferencesAndClosure(input, input);
    }

    [Theory]
    [InlineData("{\"project_object\":true}")]
    [InlineData("{\"page\":\"P\"}")]
    [InlineData("{\"device_tag\":\"K1\",\"page\":\"P\"}")]
    [InlineData("{\"locator\":{\"project\":\"X:/fixture/Line1\",\"kind\":\"connection\",\"type\":21,\"oid\":9223372036854775807,\"name\":null,\"pos\":\"1,2\"}}")]
    public void Input_TargetUnion_AcceptsEachCompleteBranch(string target)
    {
        using JsonDocument input = JsonDocument.Parse("{\"queries\":[{\"target\":" + target + ",\"properties\":[{\"property_name\":\"FORMAL_NAME\",\"index\":-2147483648}]}],\"language\":\"zh_CN\"}");
        Assert.True(Accepts(PropertyGetContract.Input, input.RootElement, PropertyGetContract.Input));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"queries\":[]}")]
    [InlineData("{\"queries\":[{\"target\":{\"project_object\":false},\"properties\":[{\"property_id\":10013}]}]}")]
    [InlineData("{\"queries\":[{\"target\":{\"page\":\"P\",\"project_object\":true},\"properties\":[{\"property_id\":10013}]}]}")]
    [InlineData("{\"queries\":[{\"target\":{\"project_object\":true,\"extra\":true},\"properties\":[{\"property_id\":10013}]}]}")]
    [InlineData("{\"queries\":[{\"target\":{\"project_object\":true},\"properties\":[{\"property_id\":0}]}]}")]
    [InlineData("{\"queries\":[{\"target\":{\"project_object\":true},\"properties\":[{\"property_id\":10013,\"property_name\":\"PROJ_DRAWINGNUMBER\"}]}]}")]
    [InlineData("{\"queries\":[{\"target\":{\"project_object\":true},\"properties\":[{\"property_id\":10013,\"extra\":true}]}]}")]
    [InlineData("{\"queries\":[{\"target\":{\"project_object\":true},\"properties\":[{\"property_id\":10013}],\"extra\":true}]}")]
    [InlineData("{\"queries\":[{\"target\":{\"locator\":{\"project\":\"P\",\"kind\":\"article\",\"type\":1,\"oid\":1}},\"properties\":[{\"property_id\":10013}]}]}")]
    public void Input_UnionConstraintsAndUnknownFields_AreRejectedByTheSchema(string json)
    {
        using JsonDocument input = JsonDocument.Parse(json);
        Assert.False(Accepts(PropertyGetContract.Input, input.RootElement, PropertyGetContract.Input));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Output_MappedRowsMatchClosedSchemaAndNullableTotals(bool detailed)
    {
        JsonElement output = PropertyGetContract.Output;
        CheckReferencesAndClosure(output, output);
        Assert.Equal(["project", "items", "total", "cursor", "truncated", "warnings"], Strings(output.GetProperty("required")));
        var rows = new JsonArray();
        foreach (string status in new[] { "value", "empty", "not_applicable", "read_refused" })
        {
            PropertyReadRow row = PropertyGetRowsTests.Row(status, status == "value" ? new PropertyTypedValue("integer", integerValue: long.MaxValue) : null);
            row.Reason = status == "read_refused" ? "internal" : null;
            row.Texts = detailed && status == "value" ?
            [
                new SnapshotText
                {
                    Language = "??_??",
                    Text = "stored",
                },
            ] : null;
            rows.Add(PropertyGetRows.Map(row, rows.Count, detailed));
        }

        var result = new JsonObject
        {
            ["project"] = "Line1",
            ["items"] = rows,
            ["total"] = null,
            ["cursor"] = "opaque",
            ["truncated"] = true,
            ["warnings"] = new JsonArray(),
            ["hint"] = "Pass cursor for the remaining rows.",
        };
        Assert.True(Accepts(output, JsonSerializer.SerializeToElement(result), output));
        result["total"] = 4;
        result["cursor"] = null;
        result["truncated"] = false;
        Assert.True(Accepts(output, JsonSerializer.SerializeToElement(result), output));
        result["execution"] = "not_started";
        Assert.False(Accepts(output, JsonSerializer.SerializeToElement(result), output));
    }

    private static void CheckReferencesAndClosure(JsonElement node, JsonElement root)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("$ref", out JsonElement reference))
            {
                Assert.Equal(JsonValueKind.Object, Resolve(reference.GetString()!, root).ValueKind);
            }
            if (node.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
            {
                Assert.False(node.GetProperty("additionalProperties").GetBoolean());
            }
            foreach (JsonProperty property in node.EnumerateObject())
            {
                CheckReferencesAndClosure(property.Value, root);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
            {
                CheckReferencesAndClosure(item, root);
            }
        }
    }

    // Covers only the keywords in the component schemas, including their reference and union constraints.
    private static bool Accepts(JsonElement schema, JsonElement value, JsonElement root)
    {
        if (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            return Accepts(Resolve(reference.GetString()!, root), value, root);
        }
        if (schema.TryGetProperty("oneOf", out JsonElement union) && union.EnumerateArray().Count(s => Accepts(s, value, root)) != 1)
        {
            return false;
        }
        if (schema.TryGetProperty("not", out JsonElement excluded) && Accepts(excluded, value, root))
        {
            return false;
        }
        if (schema.TryGetProperty("type", out JsonElement types) && !(types.ValueKind == JsonValueKind.Array ? Strings(types) : [types.GetString()!]).Any(type => type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => throw new InvalidOperationException("Unexpected schema type."),
        }))
        {
            return false;
        }
        if (schema.TryGetProperty("const", out JsonElement constant) && !JsonElement.DeepEquals(constant, value)
            || schema.TryGetProperty("enum", out JsonElement choices) && !choices.EnumerateArray().Any(c => JsonElement.DeepEquals(c, value)))
        {
            return false;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out JsonElement required) && Strings(required).Any(name => !value.TryGetProperty(name, out _)))
            {
                return false;
            }
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (properties.TryGetProperty(property.Name, out JsonElement field))
                    {
                        if (!Accepts(field, property.Value, root))
                        {
                            return false;
                        }
                    }
                    else if (schema.TryGetProperty("additionalProperties", out JsonElement additional) && additional.ValueKind == JsonValueKind.False)
                    {
                        return false;
                    }
                }
            }
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (schema.TryGetProperty("minItems", out JsonElement minimum) && value.GetArrayLength() < minimum.GetInt32()
                || schema.TryGetProperty("maxItems", out JsonElement maximum) && value.GetArrayLength() > maximum.GetInt32())
            {
                return false;
            }
            if (schema.TryGetProperty("items", out JsonElement items) && value.EnumerateArray().Any(v => !Accepts(items, v, root)))
            {
                return false;
            }
        }
        if (value.ValueKind == JsonValueKind.Number
            && (schema.TryGetProperty("minimum", out JsonElement min) && value.GetDecimal() < min.GetDecimal()
                || schema.TryGetProperty("maximum", out JsonElement max) && value.GetDecimal() > max.GetDecimal()))
        {
            return false;
        }
        if (value.ValueKind == JsonValueKind.String
            && (schema.TryGetProperty("minLength", out JsonElement minLength) && value.GetString()!.Length < minLength.GetInt32()
                || schema.TryGetProperty("maxLength", out JsonElement maxLength) && value.GetString()!.Length > maxLength.GetInt32()
                || schema.TryGetProperty("pattern", out JsonElement pattern) && !Regex.IsMatch(value.GetString()!, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))))
        {
            return false;
        }
        return true;
    }

    private static JsonElement Resolve(string reference, JsonElement root)
    {
        foreach (string segment in reference[2..].Split('/'))
        {
            root = root.GetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
        }
        return root;
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(v => v.GetString()!)];
}
