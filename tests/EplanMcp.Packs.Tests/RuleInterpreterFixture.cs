using System.Text.Json;

namespace EplanMcp.Packs.Tests;

internal sealed class RuleInterpreterFixture : IRuleFactSnapshot
{
    internal Dictionary<string, string[]> Kinds { get; } = new(StringComparer.Ordinal) { ["function"] = ["-M1"] };
    internal Dictionary<(string Locator, string Name), RuleValue> Properties { get; } = [];
    internal Dictionary<(string Locator, string Name), RuleRelation> Relations { get; } = [];
    internal Dictionary<string, RuleValue> Config { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, RuleValue> Parameters { get; } = new(StringComparer.Ordinal);
    internal Action<string>? OnProperty { get; set; }
    internal Action? OnObjects { get; set; }
    internal int Reads { get; private set; }

    public IReadOnlyList<string> Objects(string kind)
    {
        OnObjects?.Invoke();
        return Kinds.GetValueOrDefault(kind) ?? [];
    }

    public RuleValue? GetProperty(string locator, string name)
    {
        Reads++;
        OnProperty?.Invoke(locator);
        return Properties.GetValueOrDefault((locator, name));
    }

    public RuleRelation Relation(string locator, string relation) => Relations.GetValueOrDefault((locator, relation)) ?? new([], false);

    internal static RuleValue Data(string json, string kind = "text", string? unit = null, bool set = false, bool complete = true, bool inferred = false)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new(new(kind, unit, set), document.RootElement.Clone(), complete, "snapshot evidence", inferred);
    }

    internal RuleDefinition Rule(string check, string scope = "{\"literal\":true}", string kind = "function", string name = "rule/test")
    {
        using JsonDocument document = JsonDocument.Parse($$$"""{"severity":"recommended","source":"test","scope":{"kind":"{{{kind}}}","where":{{{scope}}}},"check":{{{check}}},"message":{"en":"Check failed"}}""");
        (RuleNode where, RuleNode predicate) = RuleValidator.Validate(document.RootElement,
            key => Config.GetValueOrDefault(key)?.Type, key => Parameters.GetValueOrDefault(key)?.Type);
        return new(name, "1.2.3", kind, where, predicate);
    }

    internal Task<IReadOnlyList<RuleResult>> Evaluate(RuleDefinition rule, RuleInterpreter? interpreter = null) =>
        (interpreter ?? new(new ManualRuleClock())).EvaluateAsync([rule], this, Config, Parameters, TestContext.Current.CancellationToken);
}

internal sealed class ManualRuleClock : TimeProvider
{
    internal long Ticks { get; set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Ticks;
}
