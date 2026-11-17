using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A complete read of the union of functions and PLC boxes; overlapping enumerations count an identity once.</summary>
public sealed class PlcImportState(IEnumerable<PlcImportFunction> functions, ImportPlcCounts counts)
{
    public IReadOnlyDictionary<long, PlcImportFunction> Functions { get; } = functions.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());

    public ImportPlcCounts Counts { get; } = counts;

    public string StateFingerprint() => Fingerprint.Of(Functions.Values.Select(f => string.Join("\t", f.Id.ToString(CultureInfo.InvariantCulture), f.Name, f.Category, f.IsPlcBox ? "plc" : "function"))
        .Append(string.Format(CultureInfo.InvariantCulture, "counts\t{0}\t{1}\t{2}", Counts.Functions, Counts.PlcBoxes, Counts.ArticleReferences)));
}
