using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class PlanStepExecuteSerializationTests
{
    [Theory]
    [InlineData("page.create")]
    [InlineData("page.update")]
    [InlineData("page.delete")]
    [InlineData("page.renumber")]
    [InlineData("device.update")]
    [InlineData("device.renumber")]
    [InlineData("macro.insert")]
    public void Step_AllTypedChoices_RoundTripHostAndBridgeSerializers(string op)
    {
        var args = new PlanStepExecuteArgs
        {
            PlanId = "0123456789abcdef0123456789abcdef",
            Index = 1,
            Total = 7,
            Guard = new()
            {
                Project = "Line1",
                ReadScope = new()
                {
                    Pages = ["+A1/1"]
                }
            },
            Fingerprint = new string('a', 64),
            ApprovedChanges = [new()
            {
                Action = "update",
                Kind = "page",
                Name = "+A1/1",
                NewValue = "位置;@"
            }
            ],
            ApprovedDetails = new()
            {
                ["pages"] = "+A1/1\n+A1/2"
            },
        };
        switch (op)
        {
            case "page.create":
                args.PageCreate = new()
                {
                    Project = "Line1",
                    Language = "en_US",
                    Pages = [new()
                    {
                        Name = "+A1/1"
                    }
                    ]
                };
                break;
            case "page.update":
                args.PageUpdate = new()
                {
                    Project = "Line1",
                    Pages = [new()
                    {
                        Name = "+A1/1",
                        Description = "x"
                    }
                    ]
                };
                break;
            case "page.delete":
                args.PageDelete = new()
                {
                    Project = "Line1",
                    Pages = ["+A1/1"]
                };
                break;
            case "page.renumber":
                args.PageRenumber = new()
                {
                    Project = "Line1",
                    Pages = ["+A1/1"],
                    Start = 100
                };
                break;
            case "device.update":
                args.DeviceUpdate = new()
                {
                    Project = "Line1",
                    Tag = "+A1-K1",
                    NewTag = "-K9"
                };
                break;
            case "device.renumber":
                args.DeviceRenumber = new()
                {
                    Project = "Line1",
                    Pages = ["+A1/1"],
                    Scheme = "IEC"
                };
                break;
            case "macro.insert":
                args.MacroInsert = new()
                {
                    Project = "Line1",
                    Page = "+A1/1",
                    Macro = "test.ema",
                    X = 40.001,
                    Y = 280,
                    Numbering = "keep"
                };
                args.MacroConstraint = new()
                {
                    Arguments = args.MacroInsert,
                    Description = new()
                    {
                        Sha256 = new string('b', 64),
                        ObjectCount = 5
                    },
                };
                break;
        }

        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            MissingMemberHandling = MissingMemberHandling.Error
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string host = System.Text.Json.JsonSerializer.Serialize(args, options);
        string bridge = JsonConvert.SerializeObject(JsonConvert.DeserializeObject<PlanStepExecuteArgs>(host, settings), settings);
        PlanStepExecuteArgs actual = System.Text.Json.JsonSerializer.Deserialize<PlanStepExecuteArgs>(bridge, options)!;
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(host).RootElement, System.Text.Json.JsonSerializer.SerializeToElement(actual, options)));
        Assert.DoesNotContain("approval", host, StringComparison.OrdinalIgnoreCase);
    }
}
