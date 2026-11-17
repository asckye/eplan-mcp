using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.MacroList"/> (docs/tools/eplan_macro_list.md): scans the macro directory for window macros
/// without opening any (<see cref="MacroFiles"/>), filters by path, and slices, in one main-thread call.
/// </summary>
public sealed class MacroListOperation(IMacroReader reader, TimeSpan scanBudget) : Operation<MacroListArgs, MacroListData>
{
    /// <summary>
    /// How long one call may scan. The scan runs on EPLAN's main thread like every operation, and a macro directory on
    /// a network share can be slow, so it stops here rather than hold EPLAN (tool spec 5).
    /// </summary>
    public static readonly TimeSpan DefaultScanBudget = TimeSpan.FromSeconds(2);

    internal const string NoMacroDirectory =
        "EPLAN's macro directory could not be found or read. Ask the engineer to check the macro directory in EPLAN's settings.";

    public MacroListOperation(IMacroReader reader)
        : this(reader, DefaultScanBudget)
    {
    }

    public override string Name => OperationNames.MacroList;

    protected override MacroListData Execute(MacroListArgs args)
    {
        // The local path stays in the Bridge: errors never name it (SEC-12).
        string root = reader.MacroRoot() is { } resolved && Directory.Exists(resolved)
            ? resolved
            : throw new BridgeErrorException(ErrorReason.EplanOperationFailed, NoMacroDirectory);
        MacroScan scan = MacroFiles.Scan(root, scanBudget);
        string? text = Given(args.NameContains);
        List<MacroListItem> matching =
        [
            .. scan.Paths
                .Where(p => text is null || p.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => new MacroListItem { Path = p, Name = MacroFiles.NameOf(p) }),
        ];
        return new MacroListData { Items = Paging.Slice(matching, args.Offset, args.Limit), Total = matching.Count, Incomplete = !scan.Complete };
    }
}
