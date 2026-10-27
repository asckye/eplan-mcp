using System.Globalization;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A52: project-owned library containers are never initialized, closed or disposed.</summary>
internal sealed class EplanSymbolReader : ISymbolReader
{
    public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args)
    {
        MainThread.Assert();
        var result = new List<SymbolSearchItem>();
        foreach (SymbolLibrary library in EplanProjects.ByPath(projectPath).SymbolLibraries)
        {
            if (!string.IsNullOrWhiteSpace(args.Library) && !string.Equals(args.Library!.Trim(), library.Name, StringComparison.OrdinalIgnoreCase))
            { continue; }
            foreach (Symbol symbol in library.Symbols)
            {
                string type = symbol.Type.ToString();
                string kind = SymbolKinds.From(type, type is "Function" or "BoxFunction" ? Category(symbol) : null);
                if (!SymbolKinds.Matches(args, library.Name, symbol.Name, kind))
                { continue; }
                foreach (SymbolVariant variant in symbol.Variants)
                {
                    result.Add(new SymbolSearchItem
                    {
                        Library = library.Name,
                        Name = symbol.Name,
                        Type = type,
                        Kind = kind,
                        Variant = variant.VariantNr,
                        ConnectionPoints = [.. variant.ConnectionPoints.Select(p => new SymbolConnectionPoint { Location = [p.Location.X, p.Location.Y], Direction = p.Direction.ToString() })],
                    });
                }
            }
        }

        return result;
    }

    private static string? Category(Symbol symbol)
    {
        // S43: 20188 encodes category/group/id. Read a property list once (A36/A39).
        using SymbolPropertyList properties = symbol.Properties;
        var id = (Properties.Symbol)20188;
        if (!properties.Exists(id))
        { return null; }
        using PropertyValue value = properties[id];
        return !value.IsEmpty && int.TryParse(value.ToString().Split('/')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int category)
            ? ((FunctionCategory)category).ToString() : null;
    }
}
