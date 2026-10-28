using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class ConnectionCreateContractTests
{
    static ConnectionCreateContractTests() => DiffEngine.DiffRunner.Disabled = true;

    [Fact]
    public async Task PreviewContract_MatchesReviewedSnapshot()
    {
        var fixture = new ConnectionCreateFixture();
        JObject plan = fixture.Plan();
        Validate(InputSchema(), fixture.Request);
        Validate(OutputSchema(), plan);
        JObject contract = new()
        {
            ["name"] = "eplan_connection_create",
            ["title"] = "Create connection symbols and explicit wire paths",
            ["status"] = "registered_preview_contract",
            ["profile"] = "full",
            ["description"] = "Create 1-50 connection items on one Circuit page using explicit orthogonal paths and SPECIAL symbols. Coordinates are mm, origin lower-left, y upwards; snap_to_grid defaults to true. One call commits placement then generation: 2 undo steps. Generation failure undoes placement. Writes require approval outside scratch; refusal returns APPROVAL_DENIED.",
            ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = false, ["idempotentHint"] = false, ["openWorldHint"] = false },
            ["inputSchema"] = InputSchema(),
            ["outputSchema"] = OutputSchema(),
            ["example"] = fixture.Request,
            ["dryRunExample"] = plan,
        };
        await Verify(contract.ToString(Formatting.Indented).ReplaceLineEndings("\n") + "\n", "json")
            .UseDirectory("../../Contracts.Snapshots").UseFileName("connection-create-contract");
    }

    private static JObject InputSchema()
    {
        JObject locator = Object(new()
        {
            ["project"] = Text(),
            ["page"] = Text(),
            ["type"] = Integer(1),
            ["oid"] = Integer(1),
            ["digest"] = new JObject { ["type"] = "string", ["pattern"] = "^[0-9a-f]{64}$" },
        });
        JObject pin = Object(new() { ["locator"] = locator, ["pin_index"] = Integer(0) });
        JObject wire = Object(new() { ["kind"] = Constant("wire"), ["from"] = pin, ["to"] = pin, ["points"] = Array(Point(), 2, 32) });
        JObject Node(string kind, int count) => Object(new()
        {
            ["kind"] = Constant(kind),
            ["position"] = Point(),
            ["directions"] = new JObject
            {
                ["type"] = "array",
                ["minItems"] = count,
                ["maxItems"] = count,
                ["uniqueItems"] = true,
                ["items"] = new JObject { ["type"] = "string", ["enum"] = new JArray("Up", "Down", "Left", "Right") },
            },
        });
        JObject schema = Object(new()
        {
            ["project"] = Text(),
            ["page"] = Text(),
            ["items"] = Array(new JObject { ["oneOf"] = new JArray(wire, Node("corner", 2), Node("t_node", 3), Node("potential_point", 1)) }, 1, 50),
            ["dry_run"] = new JObject { ["type"] = "boolean", ["default"] = false },
            ["snap_to_grid"] = new JObject { ["type"] = "boolean", ["default"] = true },
            ["response_format"] = new JObject { ["type"] = "string", ["enum"] = new JArray("concise", "detailed"), ["default"] = "concise" },
        }, "project page items");
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        return schema;
    }

    private static JObject OutputSchema()
    {
        JObject symbol = Object(new()
        {
            ["library"] = Constant("SPECIAL"),
            ["name"] = new JObject { ["type"] = "string", ["enum"] = new JArray("CO", "TLRO", "TLRU", "TOUL", "TOUR", "PCP") },
            ["variant"] = Integer(0),
            ["type"] = Text(),
            ["kind"] = Text(),
            ["connectionPoints"] = Array(Object(new() { ["location"] = Point(signed: true), ["direction"] = Text() }), 1, 3),
        });
        return Object(new()
        {
            ["project"] = Text(),
            ["page"] = Text(),
            ["execution"] = Constant("not_started"),
            ["dryRun"] = new JObject { ["const"] = true },
            ["coordinates"] = Array(Object(new() { ["pointer"] = Text(), ["requested"] = Point(), ["actual"] = Point() }), 1, 1600),
            ["symbols"] = Array(Object(new() { ["position"] = Point(), ["symbol"] = symbol, ["bounds"] = Array(new() { ["type"] = "number", ["minimum"] = 0 }, 4, 4) }), 0, 1500),
            ["graphicalAutoconnect"] = Array(Array(Point(), 2, 2), 0, 10000),
            ["logicalAfterGenerate"] = Array(Object(new() { ["pins"] = Array(Text(), 1, 10000), ["potentialStartIsNull"] = new JObject { ["type"] = "boolean" } }), 0, 10000),
            ["gridSize"] = new JObject { ["type"] = "number", ["exclusiveMinimum"] = 0 },
            ["pageSize"] = Array(new() { ["type"] = "number", ["exclusiveMinimum"] = 0 }, 2, 2),
            ["coverage"] = Constant("complete_page_predicted_endpoints"),
            ["logicalDuringPlacement"] = new JObject { ["const"] = 0 },
            ["generation"] = Text(),
            ["undoSteps"] = new JObject { ["const"] = 2 },
            ["fingerprint"] = new JObject { ["type"] = "string", ["pattern"] = "^[0-9a-f]{64}$" },
        });
    }

    private static JObject Object(JObject properties, string? required = null) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = properties,
        ["required"] = required is null ? new JArray(properties.Properties().Select(p => p.Name)) : new JArray(required.Split(' ')),
    };
    private static JObject Text() => new() { ["type"] = "string", ["minLength"] = 1 };
    private static JObject Integer(int minimum) => new() { ["type"] = "integer", ["minimum"] = minimum };
    private static JObject Constant(string value) => new() { ["type"] = "string", ["const"] = value };
    private static JObject Number(int minimum) => new() { ["type"] = "number", ["minimum"] = minimum, ["maximum"] = 10000 };
    private static JObject Point(bool signed = false) => Array(Number(signed ? -10000 : 0), 2, 2);
    private static JObject Array(JObject item, int minimum, int maximum) => new() { ["type"] = "array", ["items"] = item, ["minItems"] = minimum, ["maxItems"] = maximum };

    private static void Validate(JObject schema, JToken value)
    {
        if (schema["const"] is { } constant)
        { Assert.True(JToken.DeepEquals(constant, value)); }
        if (schema["enum"] is JArray options)
        { Assert.Contains(options, option => JToken.DeepEquals(option, value)); }
        if (schema["oneOf"] is JArray choices)
        {
            JObject branch = Assert.Single(choices.OfType<JObject>(), s => JToken.DeepEquals(s["properties"]!["kind"]!["const"], value["kind"]));
            Validate(branch, value);
            return;
        }
        switch ((string?)schema["type"])
        {
            case "object":
                JObject obj = Assert.IsType<JObject>(value);
                foreach (string? name in schema["required"]!.Values<string>())
                { Assert.NotNull(obj[name!]); }
                foreach (JProperty member in obj.Properties())
                {
                    Assert.NotNull(schema["properties"]![member.Name]);
                    Validate((JObject)schema["properties"]![member.Name]!, member.Value);
                }
                break;
            case "array":
                JArray array = Assert.IsType<JArray>(value);
                Assert.InRange(array.Count, (int)schema["minItems"]!, (int)schema["maxItems"]!);
                foreach (JToken item in array)
                { Validate((JObject)schema["items"]!, item); }
                if ((bool?)schema["uniqueItems"] == true)
                { Assert.Equal(array.Count, array.Distinct(JToken.EqualityComparer).Count()); }
                break;
            case "string":
                Assert.Equal(JTokenType.String, value.Type);
                if (schema["minLength"] is { } length)
                { Assert.True(value.Value<string>()!.Length >= (int)length); }
                if (schema["pattern"] is { } pattern)
                { Assert.Matches(pattern.Value<string>()!, value.Value<string>()); }
                break;
            case "number":
            case "integer":
                Assert.True(value.Type is JTokenType.Float or JTokenType.Integer);
                if ((string?)schema["type"] == "integer")
                { Assert.Equal(JTokenType.Integer, value.Type); }
                if (schema["minimum"] is { } minimum)
                { Assert.True((double)value >= (double)minimum); }
                if (schema["maximum"] is { } maximum)
                { Assert.True((double)value <= (double)maximum); }
                if (schema["exclusiveMinimum"] is { } exclusive)
                { Assert.True((double)value > (double)exclusive); }
                break;
            case "boolean":
                Assert.Equal(JTokenType.Boolean, value.Type);
                break;
        }
    }
}
