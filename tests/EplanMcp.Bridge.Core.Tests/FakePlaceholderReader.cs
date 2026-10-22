using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts.Generated;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>Returns <see cref="Placeholders"/> in this order and records what was asked for.</summary>
internal sealed class FakePlaceholderReader : IPlaceholderReader
{
    public List<PlaceholderRecord> Placeholders { get; } = [];

    public List<(string Path, string? Language)> Reads { get; } = [];

    /// <summary>A placeholder with two value sets V and mA and two variables T1, T2, like the sample project's (S29).</summary>
    public static PlaceholderRecord Sample(string page, string name = "", params string[] current) => new()
    {
        Properties = new PlaceHolderProperties { Name = name, NumberOfRecords = 2, NumberOfVariables = 2, VariableNames = ["T1", "T2"] },
        Page = page,
        ValueSets = ["V", "mA"],
        AssignedObjects = 2,
        Values =
        [
            Value("V", "T1", "0-10 V"), Value("V", "T2", "10 V"),
            Value("mA", "T1", "4-20 mA"), Value("mA", "T2", "20 mA"),
        ],
        CurrentValues = [.. (current.Length == 0 ? ["0-10 V", "10 V"] : current).Select(c => new PlaceholderText { Text = c, Raw = "??_??@" + c + ";" })],
    };

    public IReadOnlyList<PlaceholderRecord> Read(string projectPath, string? language)
    {
        Reads.Add((projectPath, language));
        return Placeholders;
    }

    private static PlaceholderValueRecord Value(string valueSet, string variable, string text) =>
        new() { ValueSet = valueSet, Variable = variable, Value = new PlaceholderText { Text = text, Raw = "??_??@" + text + ";" } };
}
