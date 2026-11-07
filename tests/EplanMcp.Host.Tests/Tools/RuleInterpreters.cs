using System.Text.RegularExpressions;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// Interpreters for tests of what rules mean. The clock stands still and patterns match without the 20 ms limit, so a
/// busy machine cannot turn a pass into Unknown (gate 2026-11-07). The limits themselves are tested in
/// EplanMcp.Packs.Tests (RuleInterpreterBudgetTests).
/// </summary>
internal static class RuleInterpreters
{
    public static RuleInterpreter Untimed() => new(new ManualTime(DateTimeOffset.UnixEpoch))
    {
        Match = (input, pattern) => Regex.IsMatch(input, pattern, RuleValidator.PatternOptions, Regex.InfiniteMatchTimeout),
    };
}
