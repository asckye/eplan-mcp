using EplanMcp.Bridge.Core.Operations;
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
        var interruptions = new InterruptionPointFixture();
        Validate(InputSchema(), interruptions.Request);
        JObject interruptionPreview = interruptions.Plan();
        Validate(OutputSchema(), interruptionPreview);
        JObject contract = new()
        {
            ["name"] = "eplan_connection_create",
            ["title"] = "Create connection symbols and explicit wire paths",
            ["status"] = "registered_preview_contract",
            ["profile"] = "full",
            ["description"] = "Create 1-50 connection items on one Circuit page using explicit orthogonal paths and SPECIAL symbols. Coordinates are mm, origin lower-left, y upwards; snap_to_grid defaults to true. Wires/nodes: 2 undo steps; cables: 6-8 with explicit core readback and automatic undo on failure. Writes require approval outside scratch; refusal returns APPROVAL_DENIED.",
            ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = false, ["idempotentHint"] = false, ["openWorldHint"] = false },
            ["inputSchema"] = InputSchema(),
            ["outputSchema"] = OutputSchema(),
            ["example"] = fixture.Request,
            ["dryRunExample"] = plan,
            ["interruptionExample"] = interruptions.Request,
            ["interruptionPreview"] = interruptionPreview,
            ["interruptionReadback"] = interruptions.Execute()["interruptionPoints"],
        };
        var cables = new CableDefinitionFixture();
        contract["cablePlanningExample"] = cables.Base.Request;
        JObject cablePlan = cables.Plan();
        Validate(InputSchema(), cables.Base.Request);
        Validate(OutputSchema(cable: true), cablePlan);
        contract["cableOutputSchema"] = OutputSchema(cable: true);
        contract["cableDryRunExample"] = cablePlan;
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
        JObject interruption = Object(new()
        {
            ["kind"] = Constant("interruption_point"),
            ["name"] = new JObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200 },
            ["position"] = Point(),
            ["direction"] = new JObject { ["type"] = "string", ["enum"] = new JArray("Up", "Down", "Left", "Right") },
            ["counterpart_pages"] = Array(Text(), 1, 20),
        });
        interruption["properties"]!["counterpart_pages"]!["uniqueItems"] = true;
        JObject cable = Object(new()
        {
            ["kind"] = Constant("cable_definition"),
            ["points"] = Array(Point(), 2, 2),
            ["designation"] = Text(),
            ["type_text"] = Text(),
            ["core_count"] = new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 50 },
            ["cross_section_mm2"] = new JObject { ["type"] = "number", ["exclusiveMinimum"] = 0 },
            ["cores"] = Array(Object(new()
            {
                ["connection"] = new JObject { ["oneOf"] = new JArray(Object(new() { ["locator"] = locator }), Object(new() { ["item_index"] = new JObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 49 } })) },
                ["label"] = Text(),
                ["colour"] = new JObject { ["type"] = "string", ["enum"] = new JArray(CableDefinitionPlanner.Colours.Split(' ')) },
            }), 1, 50),
        });
        JObject schema = Object(new()
        {
            ["project"] = Text(),
            ["page"] = Text(),
            ["items"] = Array(new JObject { ["oneOf"] = new JArray(wire, Node("corner", 2), Node("t_node", 3), Node("potential_point", 1), interruption, cable) }, 1, 50),
            ["dry_run"] = new JObject { ["type"] = "boolean", ["default"] = false },
            ["snap_to_grid"] = new JObject { ["type"] = "boolean", ["default"] = true },
            ["response_format"] = new JObject { ["type"] = "string", ["enum"] = new JArray("concise", "detailed"), ["default"] = "concise" },
        }, "project page items");
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        return schema;
    }

    private static JObject OutputSchema(bool cable = false)
    {
        JObject symbol = Object(new()
        {
            ["library"] = Constant("SPECIAL"),
            ["name"] = new JObject { ["type"] = "string", ["enum"] = new JArray("CO", "TLRO", "TLRU", "TOUL", "TOUR", "PCP", "BPF") },
            ["variant"] = Integer(0),
            ["type"] = Text(),
            ["kind"] = Text(),
            ["connectionPoints"] = Array(Object(new() { ["location"] = Point(signed: true), ["direction"] = Text() }), 1, 3),
        });
        JObject optionalInterruption = (JObject)InputSchema()["properties"]!["items"]!["items"]!["oneOf"]!
            .Single(s => (string?)s["properties"]!["kind"]!["const"] == "interruption_point").DeepClone();
        JObject placedSymbol = Object(new() { ["position"] = Point(), ["symbol"] = symbol, ["bounds"] = Array(new() { ["type"] = "number", ["minimum"] = 0 }, 4, 4), ["interruption"] = optionalInterruption }, "position symbol bounds");
        JObject counterpart = Object(new() { ["page"] = Text(), ["name"] = Text(), ["oid"] = Integer(1) });
        JObject point = Object(new()
        {
            ["page"] = Text(),
            ["name"] = Text(),
            ["oid"] = Integer(1),
            ["position"] = Point(),
            ["pin"] = Point(),
            ["direction"] = Text(),
            ["crossReference"] = new JObject { ["type"] = new JArray("string", "null") },
            ["counterparts"] = Array(counterpart, 0, 1),
        });
        JObject scope = Object(new()
        {
            ["pages"] = Array(new() { ["type"] = "object", ["additionalProperties"] = true }, 1, 21),
            ["points"] = Array(point, 0, 1000),
            ["preservedEndpoints"] = Array(Array(new(), 2, 2), 0, 10000),
        });
        JObject output = Object(new()
        {
            ["project"] = Text(),
            ["page"] = Text(),
            ["execution"] = Constant("not_started"),
            ["dryRun"] = new JObject { ["const"] = true },
            ["coordinates"] = Array(Object(new() { ["pointer"] = Text(), ["requested"] = Point(), ["actual"] = Point() }), 1, 1600),
            ["symbols"] = Array(placedSymbol, 0, 1500),
            ["graphicalAutoconnect"] = Array(Array(Point(), 2, 2), 0, 10000),
            ["logicalAfterGenerate"] = Array(Object(new() { ["pins"] = Array(Text(), 1, 10000), ["potentialStartIsNull"] = new JObject { ["type"] = "boolean" } }), 0, 10000),
            ["gridSize"] = new JObject { ["type"] = "number", ["exclusiveMinimum"] = 0 },
            ["pageSize"] = Array(new() { ["type"] = "number", ["exclusiveMinimum"] = 0 }, 2, 2),
            ["coverage"] = Constant("complete_page_predicted_endpoints"),
            ["logicalDuringPlacement"] = new JObject { ["const"] = 0 },
            ["generation"] = Text(),
            ["undoSteps"] = new JObject { ["const"] = 2 },
            ["fingerprint"] = new JObject { ["type"] = "string", ["pattern"] = "^[0-9a-f]{64}$" },
            ["affectedPages"] = Array(Text(), 1, 21),
            ["interruptionScope"] = scope,
            ["interruptionPoints"] = Array(point, 0, 100),
        }, "project page execution dryRun coordinates symbols graphicalAutoconnect logicalAfterGenerate gridSize pageSize coverage logicalDuringPlacement generation undoSteps fingerprint");
        if (cable)
        {
            output["properties"]!["undoSteps"] = new JObject { ["type"] = "integer", ["minimum"] = 6, ["maximum"] = 8 };
            output["properties"]!["cableDefinitions"] = new JObject { ["type"] = "array", ["items"] = new JObject(), ["minItems"] = 1, ["maxItems"] = 50 };
            output["properties"]!["coreImpact"] = new JObject { ["type"] = "array", ["items"] = new JObject(), ["minItems"] = 1, ["maxItems"] = 50 };
            output["properties"]!["actualCores"] = new JObject { ["type"] = "array", ["items"] = new JObject(), ["minItems"] = 1, ["maxItems"] = 50 };
            ((JArray)output["required"]!).Add("coreImpact");
        }
        return output;
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
        if (schema["type"] is JArray allowed)
        {
            Assert.Contains(value.Type == JTokenType.Null || value.Value<string>() is null ? "null" : "string", allowed.Values<string>());
            return;
        }
        if (schema["const"] is { } constant)
        { Assert.True(JToken.DeepEquals(constant, value)); }
        if (schema["enum"] is JArray options)
        { Assert.Contains(options, option => JToken.DeepEquals(option, value)); }
        if (schema["oneOf"] is JArray choices)
        {
            JObject branch = Assert.Single(choices.OfType<JObject>(), s => s["properties"]!["kind"] is { } kind
                ? JToken.DeepEquals(kind["const"], value["kind"])
                : value is JObject obj && s["required"]!.Values<string>().All(n => obj[n!] is not null) && obj.Properties().All(p => s["properties"]![p.Name] is not null));
            Validate(branch, value);
            return;
        }
        switch ((string?)schema["type"])
        {
            case "object":
                JObject obj = Assert.IsType<JObject>(value);
                if ((bool?)schema["additionalProperties"] == true)
                { break; }
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
