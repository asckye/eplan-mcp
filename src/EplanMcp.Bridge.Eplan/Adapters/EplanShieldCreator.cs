using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Shields through <c>Shield.Create(page, symbol, start, end)</c>, which chooses the variant from the corners; the overload with a variant no longer creates shields (api-notes A112).</summary>
internal sealed class EplanShieldCreator : IShieldCreator
{
    public IReadOnlyList<string> Symbols(string projectPath)
    {
        MainThread.Assert();
        return [.. SymbolsOf(projectPath).Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal)];
    }

    public ITransactionScopeFactory Transactions(string projectPath) => new Factory(projectPath);

    public ShieldObjects Create(string projectPath, string page, string symbol, string? name, ShieldCreateArgs area)
    {
        MainThread.Assert();
#pragma warning disable CA2000 // A39/A55: the project owns the shield, including after Abort.
        var shield = new Shield();
#pragma warning restore CA2000
        Page target = EplanProjects.ByPath(projectPath).Pages.Single(p => p.Name == page);
        shield.Create(target, SymbolsOf(projectPath).Single(s => s.Name == symbol), new PointD(area.X, area.Y), new PointD(area.CornerX, area.CornerY));
        if (name is not null)
        {
            shield.Name = name;
            EplanVisibleNames.Adjust(target, shield);
        }
        var objects = new ShieldObjects { Shield = shield.ObjectIdentifier };
        SymbolVariant point = EplanProjects.ByPath(projectPath).SymbolLibraries.Single(l => l.Name == "SPECIAL").Symbols.Single(s => s.Name == "CDP").Variants.Single(v => v.VariantNr == 0);
        foreach (double[] position in area.WirePoints ?? [])
        {
#pragma warning disable CA2000 // A39/A86: newly created native objects remain project-owned, including on Abort.
            var definition = new Function();
#pragma warning restore CA2000
            definition.Create(target, point);
            definition.Location = new PointD(position[0], position[1]);
            objects.DefinitionPoints.Add(definition.ObjectIdentifier);
        }
        return objects;
    }

    public ShieldCreateData Read(string projectPath, string page, ShieldObjects objects)
    {
        MainThread.Assert();
        Shield shield = EplanNativePreconditions.Valid(EplanProjects.ByPath(projectPath).Pages.Single(p => p.Name == page).AllPlacements).OfType<Shield>().Single(s => s.ObjectIdentifier == objects.Shield);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        HashSet<long> onWires = [.. EplanNativePreconditions.Valid(finder.GetConnections(null)).Where(c => EplanConnectionReader.PageOf(c)?.Name == page).SelectMany(c => c.ConnectionDefPoints).Select(p => p.ObjectIdentifier)];
        if (objects.DefinitionPoints.Any(id => !onWires.Contains(id)))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "A wire point is on no wire, so nothing was kept. Read the wires' positions with eplan_page_review and give points on them."); }
        RectangleD area = shield.GetLogicalArea();
        return new ShieldCreateData
        {
            Page = page,
            Name = shield.Name ?? string.Empty,
            Symbol = shield.SymbolVariant.SymbolName,
            Variant = shield.SymbolVariant.VariantNr,
            Bounds = [area.LowerLeft.X, area.LowerLeft.Y, area.UpperRight.X, area.UpperRight.Y],
            Connections = [.. (shield.ShieldedConnections ?? []).Select(c => EplanConnectionReader.Record(c, new TextLanguage(ISOCode.Language.L___, false))).Select(ConnectionChanges.Key).Distinct(StringComparer.Ordinal)],
        };
    }

    private static IEnumerable<Symbol> SymbolsOf(string projectPath) => EplanProjects.ByPath(projectPath).SymbolLibraries.Where(l => l.Name == "SPECIAL").SelectMany(l => l.Symbols).Where(s => s.Type == Symbol.SymbolType.Shielding);

    private sealed class Factory(string project) : ITransactionScopeFactory
    { public ITransactionScope Begin(string undoDescription) => new FunctionTransactionScope(EplanProjects.ByPath(project), undoDescription, undoCommitted: true, undoAbandoned: true); }
}
