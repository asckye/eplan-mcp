using System.Globalization;
using Eplan.EplApi.Base;
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
        Project project = EplanProjects.ByPath(projectPath);
        var inputDefinitions = new Dictionary<int, string?>();
        foreach (SymbolLibrary library in project.SymbolLibraries)
        {
            if (!string.IsNullOrWhiteSpace(args.Library) && !string.Equals(args.Library!.Trim(), library.Name, StringComparison.OrdinalIgnoreCase))
            { continue; }
            foreach (Symbol symbol in library.Symbols)
            {
                string type = symbol.Type.ToString();
                string? category = type is "Function" or "BoxFunction" ? Category(symbol) : null;
                string kind = SymbolKinds.From(type, category);
                if (!SymbolKinds.Matches(args, library.Name, symbol.Name, kind))
                { continue; }
                foreach (SymbolVariant variant in symbol.Variants)
                {
                    string? inputDefinition = null;
                    if (type == "Function" && category == "PLCTerminal")
                    {
                        int pins = variant.ConnectionPoints.Length;
                        // A52/S43: the definition depends on pin count, not the graphic; keep only DTO values within this read.
                        if (!inputDefinitions.TryGetValue(pins, out inputDefinition))
                        { inputDefinitions.Add(pins, inputDefinition = InputDefinition(project, pins)); }
                    }
                    result.Add(new SymbolSearchItem
                    {
                        Library = library.Name,
                        Name = symbol.Name,
                        Type = type,
                        Kind = kind,
                        FunctionCategory = category,
                        PlcInputDefinition = inputDefinition,
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

    private static string? InputDefinition(Project project, int pins)
    {
        // S43: generic PLC graphics need a real DI definition with the same pin count.
        foreach (FunctionDefinition definition in project.FunctionDefinitionLibrary.FunctionDefinitions.Where(d => d.FunctionCategory == FunctionCategory.PLCTerminal && d.ConnectionPoints.Length == pins))
        {
            using var name = definition.Name;
            using var group = definition.GroupName;
            string text = group.GetStringToDisplay(ISOCode.Language.L_en_US) + " " + name.GetStringToDisplay(ISOCode.Language.L_en_US);
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\bDI\b|digital\s+input|input.*digital", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            { return FormattableString.Invariant($"{definition.Group}/{definition.Id}"); }
        }
        return null;
    }
}
