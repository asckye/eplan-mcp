using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>M12 4.1: every builtin standard rule has a case that passes and a case that fails; the rest pin how scope treats empty values.</summary>
[Trait("Size", "Medium")]
public sealed class StandardRulePackTests
{
    private static readonly string[] _packs = ["iec-60204-1", "iec-81346"];
    private static readonly string[] _coveredRules =
    [
        "pe-green-yellow", "green-yellow-only-protective", "neutral-light-blue", "power-black", "dc-control-blue",
        "motor-letter", "coil-letter", "protection-letter", "switch-letter", "lamp-letter", "sensor-letter", "converter-letter", "plc-letter", "connector-letter", "cable-letter",
    ];

    public static TheoryData<string, string, string, RuleStatus> Wires => new()
    {
        { "pe-green-yellow", "PE", "GNYE", RuleStatus.Pass },
        { "pe-green-yellow", "PE", "RD", RuleStatus.Fail },
        { "pe-green-yellow", "PEN", "RD", RuleStatus.Fail },
        { "pe-green-yellow", "PE", "", RuleStatus.Unknown },
        { "pe-green-yellow", "L1", "RD", RuleStatus.NotApplicable },
        { "pe-green-yellow", "", "RD", RuleStatus.NotApplicable },
        { "green-yellow-only-protective", "PE", "GNYE", RuleStatus.Pass },
        { "green-yellow-only-protective", "L1", "GNYE", RuleStatus.Fail },
        { "green-yellow-only-protective", "L1", "BK", RuleStatus.NotApplicable },
        { "green-yellow-only-protective", "L1", "", RuleStatus.NotApplicable },
        { "neutral-light-blue", "N", "BU", RuleStatus.Pass },
        { "neutral-light-blue", "N", "BK", RuleStatus.Fail },
        { "neutral-light-blue", "NET", "BK", RuleStatus.NotApplicable },
        { "power-black", "L2", "BK", RuleStatus.Pass },
        { "power-black", "L2", "RD", RuleStatus.Fail },
        { "power-black", "L4", "RD", RuleStatus.NotApplicable },
        { "dc-control-blue", "24 V+", "BU", RuleStatus.Pass },
        { "dc-control-blue", "24V", "GY", RuleStatus.Fail },
        { "dc-control-blue", "0V", "GY", RuleStatus.Fail },
        { "dc-control-blue", "N", "GY", RuleStatus.NotApplicable },
    };

    [Theory]
    [MemberData(nameof(Wires))]
    public async Task WireColourRules_FromThePotential(string rule, string potential, string colour, RuleStatus expected)
    {
        JsonObject wire = Row();
        wire["potentialName"] = potential;
        wire["colour"] = colour;
        Assert.Equal(expected, await EvaluateAsync("iec-60204-1", rule, new JsonObject { ["connections"] = new JsonArray(wire) }));
    }

    [Theory]
    [InlineData("pe-green-yellow", "A2", "PE", "GNYE", RuleStatus.Pass)]
    [InlineData("pe-green-yellow", "PE", "A1", "RD", RuleStatus.Fail)]
    [InlineData("pe-green-yellow", "PE1", "A1", "RD", RuleStatus.NotApplicable)]
    [InlineData("neutral-light-blue", "N", "A1", "BK", RuleStatus.Fail)]
    [InlineData("power-black", "T1", "U1", "BK", RuleStatus.Pass)]
    [InlineData("power-black", "T1", "U1", "RD", RuleStatus.Fail)]
    [InlineData("power-black", "A1", "A2", "RD", RuleStatus.NotApplicable)]
    public async Task WireColourRules_FromThePinsWithoutAPotential(string rule, string from, string to, string colour, RuleStatus expected)
    {
        JsonObject wire = Row();
        wire["from"] = new JsonObject { ["device"] = "-A", ["pin"] = from };
        wire["to"] = new JsonObject { ["device"] = "-B", ["pin"] = to };
        wire["potentialName"] = "";
        wire["colour"] = colour;
        Assert.Equal(expected, await EvaluateAsync("iec-60204-1", rule, new JsonObject { ["connections"] = new JsonArray(wire) }));
    }

    [Theory]
    [InlineData("motor-letter", "Motor", "=GAA+A1-MA1", "=GAA+A1-K1")]
    [InlineData("coil-letter", "Coil", "=GAA+A1-QA1", "=GAA+A1-M1")]
    [InlineData("coil-letter", "Coil", "=GAA+A1-KF2", "=GAA+A1-Y2")]
    [InlineData("protection-letter", "Overload", "=GAA+A1-FC1", "=GAA+A1-K1")]
    [InlineData("protection-letter", "CircuitBreaker", "=GAA+A1-Q1", "=GAA+A1-K1")]
    [InlineData("switch-letter", "Switch", "=GAA+A1-SG1", "=GAA+A1-K1")]
    [InlineData("lamp-letter", "SignalLamp", "=GAA+A1-PF1", "=GAA+A1-H1")]
    [InlineData("sensor-letter", "AnalogSensor", "=GAA+A1-BL1", "=GAA+A1-S1")]
    [InlineData("converter-letter", "Converter", "=GAA+A1-TA1", "=GAA+A1-U1")]
    [InlineData("plc-letter", "PLCBox", "=GAA+A1-KE1", "=GAA+A1-U1")]
    [InlineData("connector-letter", "TerminalDefText", "=GAA+A1-XD1", "=GAA+A1-T1")]
    [InlineData("cable-letter", "Cable", "=GAA+A1-WD1", "=GAA+A1-C1")]
    public async Task LetterRules_PassTheClass_FailAnother_AndSkipOtherCategories(string rule, string category, string right, string wrong)
    {
        async Task<RuleStatus> Status(string tag, string kind, bool main = true)
        {
            JsonObject device = Row();
            device["tag"] = tag;
            device["category"] = kind;
            device["main"] = main;
            return await EvaluateAsync("iec-81346", rule, new JsonObject { ["devices"] = new JsonArray(device) });
        }
        Assert.Equal(RuleStatus.Pass, await Status(right, category));
        Assert.Equal(RuleStatus.Fail, await Status(wrong, category));
        Assert.Equal(RuleStatus.NotApplicable, await Status(wrong, "Topology"));
        Assert.Equal(RuleStatus.NotApplicable, await Status(wrong, category, main: false));
    }

    [Fact]
    public void EveryRule_IsCoveredAboveOrNamedHere()
    {
        string[] names = [.. _packs.SelectMany(pack => Directory.GetFiles(Path.Combine(Builtin(), pack, "knowledge", "rule"), "*.json")).Select(Path.GetFileNameWithoutExtension)!];
        Assert.Equal(15, names.Length);
        Assert.All(names, name => Assert.Contains(name, _coveredRules));
    }

    private static JsonObject Row() => new() { ["locator"] = new JsonObject { ["oid"] = 1 } };

    private static async Task<RuleStatus> EvaluateAsync(string pack, string rule, JsonObject actual)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Builtin(), pack, "knowledge", "rule", rule + ".json")));
        JsonElement spec = document.RootElement.GetProperty("spec");
        (RuleNode scope, RuleNode check) = RuleValidator.Validate(spec);
        var definition = new RuleDefinition(rule, "0.1.0", spec.GetProperty("scope").GetProperty("kind").GetString()!, scope, check);
        IReadOnlyList<RuleResult> results = await new RuleInterpreter().EvaluateAsync([definition], new PageReviewFacts(actual, new JsonObject()),
            new Dictionary<string, RuleValue>(), new Dictionary<string, RuleValue>(), TestContext.Current.CancellationToken);
        return Assert.Single(results).Status;
    }

    private static string Builtin()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            { return Path.Combine(directory.FullName, "packaging", "plugin-libraries", "builtin"); }
        }
        throw new InvalidOperationException("EplanMcp.slnx not found above " + AppContext.BaseDirectory);
    }
}
